package tui

// Sparkline rendering for the metrics panel: scales a numeric series into
// unicode block characters, downsampling to fit the requested width.

var sparkLevels = []rune("▁▂▃▄▅▆▇█")

// Sparkline renders values as a fixed-width block-character strip. The series
// is bucket-averaged down to width points and scaled min→max; a flat series
// renders at mid height. Returns "" for an empty series or width <= 0.
func Sparkline(values []float64, width int) string {
	if len(values) == 0 || width <= 0 {
		return ""
	}
	points := downsample(values, width)

	lo, hi := points[0], points[0]
	for _, v := range points {
		if v < lo {
			lo = v
		}
		if v > hi {
			hi = v
		}
	}

	out := make([]rune, len(points))
	for i, v := range points {
		level := len(sparkLevels) / 2
		if hi > lo {
			level = int((v - lo) / (hi - lo) * float64(len(sparkLevels)-1))
		}
		out[i] = sparkLevels[level]
	}
	return string(out)
}

// downsample bucket-averages values into at most width points.
func downsample(values []float64, width int) []float64 {
	if len(values) <= width {
		return values
	}
	out := make([]float64, width)
	for i := 0; i < width; i++ {
		start := i * len(values) / width
		end := (i + 1) * len(values) / width
		if end <= start {
			end = start + 1
		}
		sum := 0.0
		for _, v := range values[start:end] {
			sum += v
		}
		out[i] = sum / float64(end-start)
	}
	return out
}

// SeriesStats returns min/avg/max of a series (zeros for an empty one).
func SeriesStats(values []float64) (min, avg, max float64) {
	if len(values) == 0 {
		return 0, 0, 0
	}
	min, max = values[0], values[0]
	sum := 0.0
	for _, v := range values {
		if v < min {
			min = v
		}
		if v > max {
			max = v
		}
		sum += v
	}
	return min, sum / float64(len(values)), max
}
