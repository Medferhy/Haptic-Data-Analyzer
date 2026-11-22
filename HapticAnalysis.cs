using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HapticApp
{
    // One loaded recording: three columns from the CSV file.
    public class Recording
    {
        public double[] Travel { get; set; }     // "Enocder" column (travel)
        public double[] Force { get; set; }      // "Force" column
        public double[] Switching { get; set; }  // "Switching" column (analog level)
        public int Count => Travel.Length;
    }

    // Everything the calculate button should display.
    public class HapticResult
    {
        public double Fa { get; set; }  public double FaTravel { get; set; }   // peak force on the press curve
        public double Fra { get; set; } public double FraTravel { get; set; }  // valley force on the press curve
        public double Frr { get; set; } public double FrrTravel { get; set; }  // valley force on the release curve
        public double DeltaF { get; set; }                                     // Fa - Fra
        public double Tm { get; set; }                                         // travel where the switch turns ON
        public double DeltaT { get; set; }                                     // Tm - travel of the force peak
        public double SwitchOnTravel { get; set; }  public double SwitchOnForce { get; set; }
        public double SwitchOffTravel { get; set; } public double SwitchOffForce { get; set; }
        public double SwitchLevelOff { get; set; }  public double SwitchLevelOn { get; set; }
        public int TurnIndex { get; set; }                                     // index where travel is largest
    }

    public static class CsvLoader
    {
        // load the acquisition data from the file.
        public static Recording Load(string path)
        {
            var travel = new List<double>();
            var force = new List<double>();
            var sw = new List<double>();

            foreach (var line in File.ReadLines(path).Skip(1)) // skip header line
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(',');
                if (p.Length < 3) continue;

                // InvariantCulture.
                if (double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) &&
                    double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) &&
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                {
                    travel.Add(t);
                    force.Add(f);
                    sw.Add(s);
                }
            }

            return new Recording
            {
                Travel = travel.ToArray(),
                Force = force.ToArray(),
                Switching = sw.ToArray()
            };
        }
    }

    public static class HapticCalculator
    {
        // Simple moving average to remove sensor noise before looking for peaks.
        private static double[] MovingAverage(double[] x, int window)
        {
            int half = window / 2;
            var y = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                int a = Math.Max(0, i - half);
                int b = Math.Min(x.Length - 1, i + half);
                double sum = 0;
                for (int k = a; k <= b; k++) sum += x[k];
                y[i] = sum / (b - a + 1);
            }
            return y;
        }

        //  calculate Fa, Fra, Frr, Tm and the switching points.
        // minStep = how big a rise or drop (in force units) counts as a real peak or valley, not noise.
        public static HapticResult Calculate(Recording r, int smoothWindow = 61, double minStep = 0.3)
        {
            double[] f = MovingAverage(r.Force, smoothWindow);

            // The turning point is where travel is largest: before it = press, after it = release.
            int iTurn = Array.IndexOf(r.Travel, r.Travel.Max());

            // --- Press curve: first force peak (Fa), then the valley after it (Fra) ---
            int iPeak = -1, iValley = -1;

            int runMax = 0;
            for (int i = 1; i <= iTurn; i++)
            {
                if (f[i] > f[runMax]) runMax = i;
                else if (f[runMax] - f[i] >= minStep) { iPeak = runMax; break; } // force has dropped: peak confirmed
            }
            if (iPeak < 0) throw new InvalidOperationException("No force peak found on the press stroke.");

            int runMin = iPeak;
            for (int i = iPeak + 1; i <= iTurn; i++)
            {
                if (f[i] < f[runMin]) runMin = i;
                else if (f[i] - f[runMin] >= minStep) { iValley = runMin; break; } // force has risen again: valley confirmed
            }
            if (iValley < 0) throw new InvalidOperationException("No force valley found after the peak.");

            // --- Release curve: first valley after the turning point (Frr) ---
            int iRelease = -1;
            runMin = iTurn;
            for (int i = iTurn + 1; i < f.Length; i++)
            {
                if (f[i] < f[runMin]) runMin = i;
                else if (f[i] - f[runMin] >= minStep) { iRelease = runMin; break; }
            }
            if (iRelease < 0) throw new InvalidOperationException("No force valley found on the release stroke.");

            // --- Switching: the signal jumps between two levels; use the midpoint as threshold ---
            double lo = r.Switching.Min(), hi = r.Switching.Max();
            double thr = (lo + hi) / 2.0;

            int iOn = -1, iOff = -1;
            for (int i = 1; i <= iTurn; i++)
                if (r.Switching[i - 1] < thr && r.Switching[i] >= thr) { iOn = i; break; }
            for (int i = iTurn + 1; i < r.Count; i++)
                if (r.Switching[i - 1] >= thr && r.Switching[i] < thr) { iOff = i; break; }
            if (iOn < 0 || iOff < 0) throw new InvalidOperationException("Switching transition not found.");

            return new HapticResult
            {
                Fa = f[iPeak],       FaTravel = r.Travel[iPeak],
                Fra = f[iValley],    FraTravel = r.Travel[iValley],
                Frr = f[iRelease],   FrrTravel = r.Travel[iRelease],
                DeltaF = f[iPeak] - f[iValley],
                Tm = r.Travel[iOn],
                DeltaT = r.Travel[iOn] - r.Travel[iPeak],
                SwitchOnTravel = r.Travel[iOn],   SwitchOnForce = r.Force[iOn],
                SwitchOffTravel = r.Travel[iOff], SwitchOffForce = r.Force[iOff],
                SwitchLevelOff = lo, SwitchLevelOn = hi,
                TurnIndex = iTurn
            };
        }
    }
}
