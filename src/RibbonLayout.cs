namespace TimetableChecker;

/// <summary>
/// Works out how wide each piece of the day ribbon should be.
///
/// Each piece should be as wide as its share of the day (a 60-minute period is three times as wide as a
/// 20-minute break) – but never narrower than a minimum, or its label won't fit (a 10-minute registration
/// would be a sliver). Pieces that would be too narrow are fixed at their minimum, and the space left over
/// is shared out in proportion between the rest. That can push another piece under its minimum, so it repeats
/// until nothing changes. (This is sometimes called "water-filling".)
/// </summary>
public static class RibbonLayout
{
    public static double[] FitWidths(IReadOnlyList<double> weights, IReadOnlyList<double> minimums, double available)
    {
        int n = weights.Count;
        var atMinimum = new bool[n];

        while (true)
        {
            double spaceLeft = available;
            double weightLeft = 0;
            for (int i = 0; i < n; i++)
            {
                if (atMinimum[i]) spaceLeft -= minimums[i];
                else weightLeft += weights[i];
            }

            // Fix any piece whose proportional share is below its minimum, then go round again
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if (atMinimum[i]) continue;
                double share = weightLeft > 0 ? Math.Max(0, spaceLeft) * weights[i] / weightLeft : 0;
                if (share < minimums[i]) { atMinimum[i] = true; changed = true; }
            }

            if (!changed)
            {
                var widths = new double[n];
                for (int i = 0; i < n; i++)
                    widths[i] = atMinimum[i] ? minimums[i] : spaceLeft * weights[i] / weightLeft;
                return widths;
            }
            // Each time round, at least one more piece is fixed, so the loop always ends (after at most n rounds)
        }
    }
}
