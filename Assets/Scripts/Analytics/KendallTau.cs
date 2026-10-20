using System;
using System.Collections.Generic;
using System.Linq;

namespace CrimeSceneVR.Analytics
{
    /// <summary>
    /// Encapsulates the detailed result of a Kendall's Tau-b rank correlation analysis.
    /// </summary>
    public readonly struct KendallTauResult
    {
        public double Tau { get; }
        public int ConcordantPairs { get; }
        public int DiscordantPairs { get; }
        public int TiesGroundTruth { get; }
        public int TiesParticipant { get; }
        public int TotalEvaluatedPairs { get; }
        public double NormalizedScore { get; }
        public string Interpretation { get; }

        public KendallTauResult(
            double tau,
            int concordant,
            int discordant,
            int tiesGt,
            int tiesP,
            int totalPairs,
            string interpretation)
        {
            Tau = double.IsNaN(tau) ? 0.0 : Math.Clamp(tau, -1.0, 1.0);
            ConcordantPairs = concordant;
            DiscordantPairs = discordant;
            TiesGroundTruth = tiesGt;
            TiesParticipant = tiesP;
            TotalEvaluatedPairs = totalPairs;
            NormalizedScore = Math.Clamp((Tau + 1.0) / 2.0, 0.0, 1.0);
            Interpretation = interpretation;
        }

        public override string ToString()
        {
            return $"Tau-b: {Tau:F3} (Normalized: {NormalizedScore:P1}) | Concordant: {ConcordantPairs}, Discordant: {DiscordantPairs}, Ties: {TiesGroundTruth}/{TiesParticipant} | {Interpretation}";
        }
    }

    /// <summary>
    /// Computes Kendall's Tau-b rank correlation coefficient between canonical ground-truth
    /// crime scene timelines and participant-hypothesized reconstructions.
    /// Accurately handles tied ranks, omissions, duplicate submissions, and edge cases.
    /// </summary>
    public static class KendallTau
    {
        /// <summary>
        /// Calculates Kendall's Tau-b correlation between ground-truth and participant sequence.
        /// Formula: tau_b = (P - Q) / sqrt((P + Q + TiesX) * (P + Q + TiesY))
        /// </summary>
        /// <param name="groundTruth">Canonical sequence of evidence IDs in ground truth chronological order.</param>
        /// <param name="participantSequence">Sequence of evidence IDs submitted by participant.</param>
        /// <param name="penalizeOmissions">If true, omitted items are assigned the worst tied rank.</param>
        public static KendallTauResult Calculate(
            IList<string> groundTruth,
            IList<string> participantSequence,
            bool penalizeOmissions = true)
        {
            if (groundTruth == null || groundTruth.Count == 0)
            {
                return new KendallTauResult(0.0, 0, 0, 0, 0, 0, "Ground truth sequence is null or empty.");
            }

            if (participantSequence == null || participantSequence.Count == 0)
            {
                int emptyTotalPairs = groundTruth.Count * (groundTruth.Count - 1) / 2;
                return new KendallTauResult(
                    -1.0, 0, emptyTotalPairs, 0, 0, emptyTotalPairs,
                    "Participant sequence is empty (complete discordance).");
            }

            // Clean ground truth (preserving unique elements)
            var cleanGt = new List<string>(groundTruth.Count);
            foreach (var item in groundTruth)
            {
                if (!string.IsNullOrWhiteSpace(item) && !cleanGt.Contains(item))
                {
                    cleanGt.Add(item.Trim());
                }
            }

            if (cleanGt.Count < 2)
            {
                return new KendallTauResult(0.0, 0, 0, 0, 0, 0, "Trivial sequence with < 2 unique items.");
            }

            // Map ground truth items to their canonical ranks (0, 1, 2, ...)
            var gtRankMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cleanGt.Count; i++)
            {
                gtRankMap[cleanGt[i]] = i;
            }

            // Map participant submissions to ranks
            // If an item appears multiple times, assign the average of its positions (standard tie handling)
            var participantPositions = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < participantSequence.Count; i++)
            {
                string id = participantSequence[i]?.Trim();
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (!participantPositions.TryGetValue(id, out var list))
                {
                    list = new List<int>();
                    participantPositions[id] = list;
                }
                list.Add(i);
            }

            List<string> evaluatedItems;
            if (penalizeOmissions)
            {
                evaluatedItems = new List<string>(cleanGt);
            }
            else
            {
                evaluatedItems = cleanGt.Where(id => participantPositions.ContainsKey(id)).ToList();
            }

            int n = evaluatedItems.Count;
            if (n < 2)
            {
                return new KendallTauResult(0.0, 0, 0, 0, 0, 0, "Fewer than 2 common items between ground truth and submission.");
            }

            double[] xRanks = new double[n]; // Ground truth
            double[] yRanks = new double[n]; // Participant
            double worstParticipantRank = participantSequence.Count + 1.0;

            for (int i = 0; i < n; i++)
            {
                string item = evaluatedItems[i];
                xRanks[i] = gtRankMap[item];

                if (participantPositions.TryGetValue(item, out var positions))
                {
                    yRanks[i] = positions.Average();
                }
                else
                {
                    yRanks[i] = worstParticipantRank;
                }
            }

            // Calculate pairwise concordance, discordance, and ties
            int p = 0; // Concordant
            int q = 0; // Discordant
            int tiesX = 0;
            int tiesY = 0;
            int totalPairs = n * (n - 1) / 2;

            for (int i = 0; i < n - 1; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double diffX = xRanks[i] - xRanks[j];
                    double diffY = yRanks[i] - yRanks[j];

                    if (Math.Abs(diffX) < 1e-9 && Math.Abs(diffY) < 1e-9)
                    {
                        // Tied in both
                        continue;
                    }
                    else if (Math.Abs(diffX) < 1e-9)
                    {
                        tiesX++;
                    }
                    else if (Math.Abs(diffY) < 1e-9)
                    {
                        tiesY++;
                    }
                    else if ((diffX > 0 && diffY > 0) || (diffX < 0 && diffY < 0))
                    {
                        p++;
                    }
                    else
                    {
                        q++;
                    }
                }
            }

            // Include duplicate submission occurrences in participant sequence as tied pairs (k*(k-1)/2)
            foreach (var kvp in participantPositions)
            {
                int count = kvp.Value.Count;
                if (count > 1)
                {
                    tiesY += count * (count - 1) / 2;
                }
            }

            // Compute Kendall's Tau-b formula:
            // tau_b = (P - Q) / sqrt((P + Q + TiesX) * (P + Q + TiesY))
            double denomX = p + q + tiesX;
            double denomY = p + q + tiesY;
            double denominator = Math.Sqrt(denomX * denomY);

            double tau = (denominator > 1e-9) ? ((p - q) / denominator) : 0.0;

            string interpretation;
            if (tau > 0.99) interpretation = "Perfect chronological agreement.";
            else if (tau >= 0.70) interpretation = "Strong chronological reconstruction.";
            else if (tau >= 0.30) interpretation = "Moderate chronological agreement.";
            else if (tau > -0.30) interpretation = "Weak or random reconstruction ordering.";
            else if (tau > -0.70) interpretation = "Moderate inverse sequencing.";
            else interpretation = "Strong inverse or reverse chronological ordering.";

            return new KendallTauResult(tau, p, q, tiesX, tiesY, totalPairs, interpretation);
        }
    }
}
