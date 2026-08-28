package commands

import (
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
)

// withGithubStub points release fetching at a stub server for one test.
func withGithubStub(t *testing.T, handler http.Handler) {
	t.Helper()
	srv := httptest.NewServer(handler)
	orig := githubAPIBase
	githubAPIBase = srv.URL
	t.Cleanup(func() {
		githubAPIBase = orig
		srv.Close()
	})
}

func TestFetchBestRelease_StableUsesLatestEndpoint(t *testing.T) {
	mux := http.NewServeMux()
	mux.HandleFunc("/repos/freeman412/mineos-sveltekit/releases/latest", func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, `{"tag_name":"v1.2.0","prerelease":false,"assets":[]}`)
	})
	mux.HandleFunc("/repos/freeman412/mineos-sveltekit/releases", func(w http.ResponseWriter, _ *http.Request) {
		t.Error("stable check must not hit the all-releases endpoint")
	})
	withGithubStub(t, mux)

	release, err := fetchBestRelease(false)
	if err != nil {
		t.Fatal(err)
	}
	if release.TagName != "v1.2.0" || release.Prerelease {
		t.Fatalf("wrong release: %+v", release)
	}
}

func TestFetchBestRelease_PrereleasePicksNewestOfAll(t *testing.T) {
	mux := http.NewServeMux()
	mux.HandleFunc("/repos/freeman412/mineos-sveltekit/releases", func(w http.ResponseWriter, _ *http.Request) {
		// GitHub sorts newest-first; the newest here is a beta.
		_, _ = io.WriteString(w, `[
			{"tag_name":"v1.2.0-beta.5","prerelease":true,"assets":[]},
			{"tag_name":"v1.1.0","prerelease":false,"assets":[]}
		]`)
	})
	withGithubStub(t, mux)

	release, err := fetchBestRelease(true)
	if err != nil {
		t.Fatal(err)
	}
	if release.TagName != "v1.2.0-beta.5" || !release.Prerelease {
		t.Fatalf("newest (pre-)release not selected: %+v", release)
	}
}

func TestFetchBestRelease_NoReleases(t *testing.T) {
	mux := http.NewServeMux()
	mux.HandleFunc("/repos/freeman412/mineos-sveltekit/releases/latest", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	mux.HandleFunc("/repos/freeman412/mineos-sveltekit/releases", func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, `[]`)
	})
	withGithubStub(t, mux)

	if _, err := fetchBestRelease(false); !errors.Is(err, errNoReleases) {
		t.Fatalf("404 must map to errNoReleases, got %v", err)
	}
	if _, err := fetchBestRelease(true); !errors.Is(err, errNoReleases) {
		t.Fatalf("empty list must map to errNoReleases, got %v", err)
	}
}

func TestFetchBestRelease_ServerErrorSurfaces(t *testing.T) {
	withGithubStub(t, http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusInternalServerError)
	}))
	if _, err := fetchBestRelease(false); err == nil {
		t.Fatal("500 must surface as an error")
	}
}
