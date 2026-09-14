using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Holds the fixed order <see cref="Tasks.OrderRecallStep"/> reads and later checks against
    /// (Tasks/ARCHITECTURE.md §7.3) - a permutation of 1..sequenceLength, generated once for the whole
    /// match and never changed, so every player who reads the Ledger sees the same order.
    /// </summary>
    [RequireComponent(typeof(TaskLocation))]
    public class LedgerStation : MonoBehaviour
    {
        [Tooltip("How many distinct entries the fixed order has for this match.")]
        [SerializeField] private int sequenceLength = 3;

        private List<int> sequence;

        /// <summary>The fixed order for this match, 1..sequenceLength each appearing once. Read-only.</summary>
        public IReadOnlyList<int> Sequence => sequence;

        private void Awake()
        {
            sequence = GenerateSequence(sequenceLength);
        }

        // Fisher-Yates shuffle of 1..length - a true order (distinct entries), not N independent
        // random picks, which could repeat a value and make some positions indistinguishable.
        private static List<int> GenerateSequence(int length)
        {
            List<int> values = new List<int>(Mathf.Max(0, length));
            for (int i = 1; i <= length; i++) values.Add(i);

            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }

            return values;
        }
    }
}
