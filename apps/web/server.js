// Custom server for handling WebSocket proxying
// This wraps the SvelteKit handler and proxies WebSocket connections to the API

import { createServer } from 'http';
import { WebSocketServer, WebSocket } from 'ws';
import { handler } from './build/handler.js';

const PORT = parseInt(process.env.PORT || '3000', 10);
const HOST = process.env.HOST || '0.0.0.0';

// API base URL for proxying
const API_BASE = process.env.PRIVATE_API_BASE_URL || process.env.INTERNAL_API_URL || 'http://api:5078';
const API_KEY = process.env.PRIVATE_API_KEY || '';

// Convert HTTP URL to WebSocket URL
const WS_BASE = API_BASE.replace(/^http/, 'ws');

// Paths that should be proxied as WebSocket
const WS_PROXY_PATHS = [
	'/api/v1/admin/shell/ws'
];

/**
 * Parse auth token from cookie string
 */
function parseAuthToken(cookieHeader) {
	if (!cookieHeader) return null;
	const match = cookieHeader.match(/auth_token=([^;]+)/);
	return match?.[1] || null;
}

/**
 * Check if a path should be WebSocket proxied
 */
function shouldProxyWebSocket(path) {
	return WS_PROXY_PATHS.some(p => path === p || path.startsWith(p + '?'));
}

// Create HTTP server
const server = createServer(handler);

// Create WebSocket server (no server - we'll handle upgrades manually)
const wss = new WebSocketServer({ noServer: true });

// Handle WebSocket upgrade requests
server.on('upgrade', (req, socket, head) => {
	const url = new URL(req.url || '/', `http://${req.headers.host}`);
	const path = url.pathname;

	if (shouldProxyWebSocket(path)) {
		// Proxy this WebSocket to the API
		const token = parseAuthToken(req.headers.cookie);
		const targetUrl = `${WS_BASE}${path}${url.search}`;

		console.log(`[WS Proxy] Connecting to: ${targetUrl}`);

		// Build headers for upstream
		const upstreamHeaders = {};
		if (API_KEY) {
			upstreamHeaders['X-Api-Key'] = API_KEY;
		}
		if (token) {
			upstreamHeaders['Authorization'] = `Bearer ${token}`;
		}
		// Forward WebSocket protocol if present
		if (req.headers['sec-websocket-protocol']) {
			upstreamHeaders['Sec-WebSocket-Protocol'] = req.headers['sec-websocket-protocol'];
		}

		// Connect to upstream WebSocket
		const upstream = new WebSocket(targetUrl, {
			headers: upstreamHeaders
		});

		upstream.on('open', () => {
			console.log(`[WS Proxy] Connected to upstream`);

			// Complete the WebSocket handshake with the client
			wss.handleUpgrade(req, socket, head, (clientWs) => {
				// Pipe messages between client and upstream
				clientWs.on('message', (data, isBinary) => {
					if (upstream.readyState === WebSocket.OPEN) {
						upstream.send(data, { binary: isBinary });
					}
				});

				upstream.on('message', (data, isBinary) => {
					if (clientWs.readyState === WebSocket.OPEN) {
						clientWs.send(data, { binary: isBinary });
					}
				});

				// Handle close
				clientWs.on('close', (code, reason) => {
					console.log(`[WS Proxy] Client closed: ${code}`);
					upstream.close(code, reason);
				});

				upstream.on('close', (code, reason) => {
					console.log(`[WS Proxy] Upstream closed: ${code}`);
					clientWs.close(code, reason);
				});

				// Handle errors
				clientWs.on('error', (err) => {
					console.error(`[WS Proxy] Client error:`, err.message);
					upstream.close();
				});

				upstream.on('error', (err) => {
					console.error(`[WS Proxy] Upstream error:`, err.message);
					clientWs.close();
				});
			});
		});

		upstream.on('error', (err) => {
			console.error(`[WS Proxy] Failed to connect to upstream:`, err.message);
			socket.destroy();
		});
	} else {
		// Not a proxied path - destroy the connection
		// SvelteKit doesn't handle WebSocket upgrades
		console.log(`[WS] Rejecting WebSocket upgrade for: ${path}`);
		socket.write('HTTP/1.1 404 Not Found\r\n\r\n');
		socket.destroy();
	}
});

// Start server
server.listen(PORT, HOST, () => {
	console.log(`Listening on http://${HOST}:${PORT}`);
});
