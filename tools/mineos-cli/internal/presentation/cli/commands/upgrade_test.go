package commands

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func writeTempAsset(t *testing.T, content string) string {
	t.Helper()
	p := filepath.Join(t.TempDir(), "asset.zip")
	if err := os.WriteFile(p, []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
	return p
}

func sha256Hex(s string) string {
	h := sha256.Sum256([]byte(s))
	return hex.EncodeToString(h[:])
}

func checksumsServer(t *testing.T, body string) *httptest.Server {
	t.Helper()
	return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, body)
	}))
}

func TestVerifyChecksum_Match(t *testing.T) {
	content := "fake-archive-bytes"
	path := writeTempAsset(t, content)
	asset := "mineos-cli_linux_amd64.zip"
	srv := checksumsServer(t, fmt.Sprintf("%s  %s\n", sha256Hex(content), asset))
	defer srv.Close()

	rel := &githubRelease{Assets: []githubAsset{{Name: checksumsAsset, BrowserDownloadURL: srv.URL}}}
	if err := verifyChecksum(io.Discard, path, asset, rel); err != nil {
		t.Fatalf("expected match, got %v", err)
	}
}

func TestVerifyChecksum_Mismatch(t *testing.T) {
	path := writeTempAsset(t, "real-bytes")
	asset := "mineos-cli_linux_amd64.zip"
	// checksum for different content — must be rejected.
	srv := checksumsServer(t, fmt.Sprintf("%s  %s\n", sha256Hex("tampered"), asset))
	defer srv.Close()

	rel := &githubRelease{Assets: []githubAsset{{Name: checksumsAsset, BrowserDownloadURL: srv.URL}}}
	if err := verifyChecksum(io.Discard, path, asset, rel); err == nil {
		t.Fatal("expected mismatch error, got nil")
	}
}

func TestVerifyChecksum_NoChecksumsAssetWarnsAndPasses(t *testing.T) {
	path := writeTempAsset(t, "bytes")
	rel := &githubRelease{Assets: []githubAsset{{Name: "mineos-cli_linux_amd64.zip"}}}
	var out strings.Builder
	if err := verifyChecksum(&out, path, "mineos-cli_linux_amd64.zip", rel); err != nil {
		t.Fatalf("expected nil (warn + proceed), got %v", err)
	}
	if !strings.Contains(out.String(), "no checksums.txt") {
		t.Fatalf("expected a warning about missing checksums, got %q", out.String())
	}
}

func TestVerifyChecksum_AssetNotListed(t *testing.T) {
	path := writeTempAsset(t, "bytes")
	asset := "mineos-cli_linux_amd64.zip"
	// checksums present but for a different asset only.
	srv := checksumsServer(t, fmt.Sprintf("%s  %s\n", sha256Hex("x"), "mineos-cli_windows_amd64.zip"))
	defer srv.Close()

	rel := &githubRelease{Assets: []githubAsset{{Name: checksumsAsset, BrowserDownloadURL: srv.URL}}}
	if err := verifyChecksum(io.Discard, path, asset, rel); err == nil {
		t.Fatal("expected error when the asset is not listed in checksums, got nil")
	}
}
