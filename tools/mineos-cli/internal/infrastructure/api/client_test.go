package api

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestListServers_DecodesRichFieldsAndDerivesStatus(t *testing.T) {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/v1/host/servers", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`[
			{"name":"lobby","up":true,"playersOnline":3,"playersMax":20,"memoryBytes":536870912,"needsRestart":false},
			{"name":"survival","up":false,"needsRestart":true}
		]`))
	})
	srv := httptest.NewServer(mux)
	defer srv.Close()

	servers, err := NewClient(srv.URL, "k").ListServers(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if len(servers) != 2 {
		t.Fatalf("want 2 servers, got %d", len(servers))
	}
	if servers[0].Status != "running" || servers[1].Status != "stopped" {
		t.Fatalf("status derivation wrong: %q %q", servers[0].Status, servers[1].Status)
	}
	if servers[0].PlayersOnline == nil || *servers[0].PlayersOnline != 3 {
		t.Fatal("playersOnline not decoded")
	}
	if servers[0].MemoryBytes == nil || *servers[0].MemoryBytes != 536870912 {
		t.Fatal("memoryBytes not decoded")
	}
	if !servers[1].NeedsRestart {
		t.Fatal("needsRestart not decoded")
	}
}

func TestListServers_MissingKey(t *testing.T) {
	if _, err := NewClient("http://example.invalid", "").ListServers(context.Background()); err != ErrApiKeyMissing {
		t.Fatalf("want ErrApiKeyMissing, got %v", err)
	}
}

func TestStreamPerformance_ParsesSSE(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		_, _ = io.WriteString(w, `data: {"isRunning":true,"cpuPercent":12.5,"ramUsedMb":512,"ramTotalMb":1024,"tps":19.9,"playerCount":2}`+"\n\n")
		_, _ = io.WriteString(w, `data: {"isRunning":true,"cpuPercent":30,"ramUsedMb":600,"ramTotalMb":1024,"tps":null,"playerCount":0}`+"\n\n")
	}))
	defer srv.Close()

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	samples, _ := NewClient(srv.URL, "k").StreamPerformance(ctx, "lobby")

	first, ok := <-samples
	if !ok {
		t.Fatal("expected a first sample")
	}
	if !first.IsRunning || first.CpuPercent != 12.5 || first.RamUsedMb != 512 || first.PlayerCount != 2 {
		t.Fatalf("bad first sample: %+v", first)
	}
	if first.Tps == nil || *first.Tps != 19.9 {
		t.Fatal("tps not decoded on first sample")
	}
	second, ok := <-samples
	if !ok {
		t.Fatal("expected a second sample")
	}
	if second.Tps != nil {
		t.Fatalf("expected nil tps on second sample, got %v", *second.Tps)
	}
}

func TestPerformanceHistory_DecodesAndPassesMinutes(t *testing.T) {
	var gotMinutes string
	mux := http.NewServeMux()
	mux.HandleFunc("/api/v1/servers/lobby/performance/history", func(w http.ResponseWriter, r *http.Request) {
		gotMinutes = r.URL.Query().Get("minutes")
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`[
			{"serverName":"lobby","timestamp":"2026-07-22T00:00:00Z","isRunning":true,"cpuPercent":10,"ramUsedMb":500,"ramTotalMb":1024,"tps":20.0,"playerCount":1},
			{"serverName":"lobby","timestamp":"2026-07-22T00:01:00Z","isRunning":true,"cpuPercent":20,"ramUsedMb":510,"ramTotalMb":1024,"tps":null,"playerCount":1}
		]`))
	})
	srv := httptest.NewServer(mux)
	defer srv.Close()

	samples, err := NewClient(srv.URL, "k").PerformanceHistory(context.Background(), "lobby", 30)
	if err != nil {
		t.Fatal(err)
	}
	if gotMinutes != "30" {
		t.Fatalf("minutes not passed, got %q", gotMinutes)
	}
	if len(samples) != 2 || samples[0].Tps == nil || *samples[0].Tps != 20.0 {
		t.Fatalf("samples not decoded: %+v", samples)
	}
	if samples[0].Timestamp.IsZero() {
		t.Fatal("timestamp not decoded")
	}
	if samples[1].Tps != nil {
		t.Fatal("null tps must stay nil")
	}
}

func TestPerformanceHistory_RequiresName(t *testing.T) {
	if _, err := NewClient("http://example.invalid", "k").PerformanceHistory(context.Background(), " ", 30); err == nil {
		t.Fatal("want error for empty server name")
	}
}

func TestListServers_InvalidKeyMapsAuthErrors(t *testing.T) {
	for _, status := range []int{http.StatusUnauthorized, http.StatusForbidden} {
		srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
			w.WriteHeader(status)
		}))
		if _, err := NewClient(srv.URL, "bad").ListServers(context.Background()); err != ErrApiKeyInvalid {
			t.Fatalf("status %d: want ErrApiKeyInvalid, got %v", status, err)
		}
		srv.Close()
	}
}

func TestStreamConsoleLogs_ParsesSSEAndSkipsJunk(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		_, _ = io.WriteString(w, ": comment to ignore\n")
		_, _ = io.WriteString(w, "data: {\"timestamp\":\"2026-07-22T00:00:00Z\",\"message\":\"hello\"}\n\n")
		_, _ = io.WriteString(w, "data: not-json\n\n")
		_, _ = io.WriteString(w, "data: {\"timestamp\":\"2026-07-22T00:00:01Z\",\"message\":\"world\"}\n\n")
	}))
	defer srv.Close()

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	logs, _ := NewClient(srv.URL, "k").StreamConsoleLogs(ctx, "lobby", "")

	var got []string
	for entry := range logs {
		got = append(got, entry.Message)
	}
	if len(got) != 2 || got[0] != "hello" || got[1] != "world" {
		t.Fatalf("SSE parse wrong: %v", got)
	}
}
