package api

import (
	"context"
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
