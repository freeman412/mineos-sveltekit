package tui

import "time"

// Banner is the ASCII art logo for MineOS
const Banner = `  __  __ _             ___  ____
 |  \/  (_)_ __   ___ / _ \/ ___|
 | |\/| | | '_ \ / _ \ | | \___ \
 | |  | | | | | |  __/ |_| |___) |
 |_|  |_|_|_| |_|\___|\___/|____/ `

const BannerTagline = "Minecraft Server Management"

const MinecraftCat = `                                                                                              :::::-
                                                         --                                 :::::-++
                                                     ===-------                          ------+**++
                                                 ==========-------=                   =----=+*****
                                              --================----=*              -----+******
                                           ===-----======++++====+****+           -----*****#
                                       ----===++==----===--=++*#***********    ----=+*****#
                                   +==--------========---=+*####*************+=--=****##
                                ====++===-----========+***######******##************#
                            =----===========-----==*####**######******     *******#
           -----=+    ----===+==-----==========+****####**######******       +**
         +==-=+*** +=--------======-------=+*###****####****####******
        ++++*****=====---=+*==----======****####****####****####******
    ==----=+**+=--=---=+++==--------+*#*****####****####****##********
=--=======---=====++++++++======+*#**##*****###******#****************
+==---=============++++==+**+***###**###****###**********************#
+++**+=----==========+*####%###*###**###****##*********************
-=+*****+=----===+*########%%##*###**###***************************
=-+##******+==+*###########%%##*###**###*******************++******
++=+#*++*#*++***#########*#%###*###*******************+*++===******
+=----=+*#*==+**#########*#%###*###***************+=---+===-=+*****
****+=---=+==+**####*####*#%%##*###********+*****====--+----=+*+***
++*****+***+++**####*****#%%%##*###**********     ===--+===-=+++*++
++++++*****++***##****+=+####***##*****##             -+===-=++++++
 +++++++***++******+=-===+************                    =-=+***
     +++***++**##====-===+************
                   ======+***+++******
                   ======++++==+******
                   ======+====-=****++
                   =----=+======++++++
                     ---=+======++++++
                          =----=++++*+
                             --=+**`

// Log buffer and streaming constants
const (
	MaxLogLines          = 5000 // Increased buffer size
	DefaultDockerLogTail = 200
	LogRetryDelay        = 2 * time.Second
	MaxLogRetries        = 3
)

// Streaming constants
const (
	StreamingBufferSize = 100
	ScannerMaxBuffer    = 1024 * 1024 // 1MB
)

// Timeout constants
const (
	HealthPollInterval = 10 * time.Second // Re-check API when unhealthy
)

// Metrics sparkline constants
const (
	PerfHistoryMinutes    = 30  // Backfill window for the metrics panel
	MaxPerfHistorySamples = 240 // Cap on retained samples (backfill + live)
	SparklineWidth        = 30  // Characters per sparkline strip
)

// UI layout constants
const (
	SidebarWidth     = 20
	MinContentHeight = 5
	// Minimum terminal dimensions below which the TUI renders a "resize" notice
	// instead of a layout. Guards against negative content widths (which would
	// panic strings.Repeat). Tunable.
	MinTerminalWidth  = 40
	MinTerminalHeight = 10
)

// Default source for docker logs
const DefaultDockerLogSource = "all"

// Minecraft log types
var MinecraftLogTypes = []string{"combined", "server", "java", "crash"}
