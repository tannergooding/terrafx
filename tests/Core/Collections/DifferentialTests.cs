// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TerraFX.Collections;

namespace TerraFX.UnitTests.Collections;

/// <summary>Provides differential tests comparing the value collections against their BCL equivalents.</summary>
internal static class DifferentialTests
{
    private const int QuickSeedCount = 4;
    private const int ExhaustiveSeedCount = 500;
    private const int OperationCount = 60;

    // The quick tier is fixed so an unrelated change can never fail on a newly generated sequence
    private const int QuickSeed = 42;

    // A failure reports a seed that can be replayed by setting this variable
    private const string ReplaySeedVariable = "TERRAFX_REPLAY_SEED";

    private static readonly int? s_replaySeed = int.TryParse(Environment.GetEnvironmentVariable(ReplaySeedVariable), CultureInfo.InvariantCulture, out var parsedSeed) ? parsedSeed : null;

    [Test]
    public static void QueueTest() => CompareQueue(QuickSeedCount, QuickSeed);

    [Test]
    public static void StackTest() => CompareStack(QuickSeedCount, QuickSeed);

    [Test]
    public static void ListTest() => CompareList(QuickSeedCount, QuickSeed);

    [Test]
    public static void DictionaryTest() => CompareDictionary(QuickSeedCount, QuickSeed);

    [Test]
    public static void LinkedListTest() => CompareLinkedList(QuickSeedCount, QuickSeed);

    [Test]
    [Explicit]
    [Category("OuterLoop")]
    public static void ExhaustiveTest()
    {
        CompareQueue(ExhaustiveSeedCount, Random.Shared.Next());
        CompareStack(ExhaustiveSeedCount, Random.Shared.Next());
        CompareList(ExhaustiveSeedCount, Random.Shared.Next());
        CompareDictionary(ExhaustiveSeedCount, Random.Shared.Next());
        CompareLinkedList(ExhaustiveSeedCount, Random.Shared.Next());
    }

    /// <summary>Replays the same randomly seeded operation sequence against both implementations and compares the transcripts.</summary>
    private static void Compare(Func<Random, string> oracle, Func<Random, string> actual, int seedCount, int seedSourceSeed)
    {
        if (s_replaySeed is int replaySeed)
        {
            CompareSeed(oracle, actual, replaySeed);
            return;
        }

        var seedSource = new Random(seedSourceSeed);

        for (var iteration = 0; iteration < seedCount; iteration++)
        {
            CompareSeed(oracle, actual, seedSource.Next());
        }
    }

    /// <summary>Replays a single seeded operation sequence against both implementations and compares the transcripts.</summary>
    private static void CompareSeed(Func<Random, string> oracle, Func<Random, string> actual, int seed)
    {
        var hint = SeedHint(seed);
        var expected = oracle(new Random(seed));

        string result;

        try
        {
            result = actual(new Random(seed));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(hint, exception);
        }

        Assert.That(result, Is.EqualTo(expected), hint);
    }

    private static string SeedHint(int seed) => $"seed {seed} (replay with {ReplaySeedVariable}={seed})";

    private static void CompareQueue(int seedCount, int seed)
    {
        Compare(static (rng) =>
        {
            var log = new StringBuilder();
            var queue = new Queue<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);

                switch (rng.Next(6))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        queue.Enqueue(value);
                        break;
                    }

                    case 3:
                    {
                        _ = log.Append(queue.TryDequeue(out var item) ? item : -1);
                        break;
                    }

                    case 4:
                    {
                        _ = log.Append(queue.TryPeek(out var item) ? item : -1);
                        break;
                    }

                    case 5:
                    {
                        var items = queue.ToList();
                        _ = log.Append(items.Remove(value));

                        queue.Clear();

                        foreach (var item in items)
                        {
                            queue.Enqueue(item);
                        }
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        queue.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        _ = queue.EnsureCapacity(queue.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(queue.Contains(value));
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', queue).Append(']');
            }

            return log.ToString();
        },
        static (rng) =>
        {
            var log = new StringBuilder();
            var queue = new ValueQueue<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);

                switch (rng.Next(6))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        queue.Enqueue(value);
                        break;
                    }

                    case 3:
                    {
                        _ = log.Append(queue.TryDequeue(out var item) ? item : -1);
                        break;
                    }

                    case 4:
                    {
                        _ = log.Append(queue.TryPeek(out var item) ? item : -1);
                        break;
                    }

                    case 5:
                    {
                        _ = log.Append(queue.Remove(value));
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        queue.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        queue.EnsureCapacity(queue.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(queue.Contains(value));
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', queue.ToArray()).Append(']');
            }

            return log.ToString();
        }, seedCount, seed);
    }

    private static void CompareStack(int seedCount, int seed)
    {
        Compare(static (rng) =>
        {
            var log = new StringBuilder();
            var stack = new Stack<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);

                switch (rng.Next(5))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        stack.Push(value);
                        break;
                    }

                    case 3:
                    {
                        _ = log.Append(stack.TryPop(out var item) ? item : -1);
                        break;
                    }

                    case 4:
                    {
                        _ = log.Append(stack.TryPeek(out var item) ? item : -1);
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        stack.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        _ = stack.EnsureCapacity(stack.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(stack.Contains(value));
                        break;
                    }

                    case 3:
                    {
                        var destination = new int[stack.Count];
                        stack.CopyTo(destination, 0);

                        _ = log.Append('<').AppendJoin(',', destination).Append('>');
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', stack).Append(']');
            }

            return log.ToString();
        },
        static (rng) =>
        {
            var log = new StringBuilder();
            var stack = new ValueStack<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);

                switch (rng.Next(5))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        stack.Push(value);
                        break;
                    }

                    case 3:
                    {
                        _ = log.Append(stack.TryPop(out var item) ? item : -1);
                        break;
                    }

                    case 4:
                    {
                        _ = log.Append(stack.TryPeek(out var item) ? item : -1);
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        stack.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        stack.EnsureCapacity(stack.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(stack.Contains(value));
                        break;
                    }

                    case 3:
                    {
                        var destination = new int[stack.Count];
                        stack.CopyTo(destination);

                        _ = log.Append('<').AppendJoin(',', destination).Append('>');
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', stack.ToArray()).Append(']');
            }

            return log.ToString();
        }, seedCount, seed);
    }

    private static void CompareList(int seedCount, int seed)
    {
        Compare(static (rng) =>
        {
            var log = new StringBuilder();
            var list = new List<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);
                var index = rng.Next(list.Count + 1);

                switch (rng.Next(6))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        list.Add(value);
                        break;
                    }

                    case 3:
                    {
                        list.Insert(index, value);
                        break;
                    }

                    case 4:
                    {
                        if (index != list.Count)
                        {
                            list.RemoveAt(index);
                        }
                        break;
                    }

                    case 5:
                    {
                        _ = log.Append(list.Remove(value)).Append(list.IndexOf(value));
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        list.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        _ = list.EnsureCapacity(list.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(list.Contains(value));
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', list).Append(']');
            }

            return log.ToString();
        },
        static (rng) =>
        {
            var log = new StringBuilder();
            var list = new ValueList<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);
                var index = rng.Next(list.Count + 1);

                switch (rng.Next(6))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        list.Add(value);
                        break;
                    }

                    case 3:
                    {
                        list.Insert(index, value);
                        break;
                    }

                    case 4:
                    {
                        if (index != list.Count)
                        {
                            list.RemoveAt(index);
                        }
                        break;
                    }

                    case 5:
                    {
                        _ = log.Append(list.Remove(value)).Append(list.IndexOf(value));
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        list.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        list.EnsureCapacity(list.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(list.Contains(value));
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', list.ToArray()).Append(']');
            }

            return log.ToString();
        }, seedCount, seed);
    }

    private static void CompareDictionary(int seedCount, int seed)
    {
        Compare(static (rng) =>
        {
            var log = new StringBuilder();
            var dictionary = new Dictionary<int, int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var key = rng.Next(12);
                var value = rng.Next(20);

                switch (rng.Next(5))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        dictionary[key] = value;
                        break;
                    }

                    case 3:
                    {
                        _ = log.Append(dictionary.Remove(key));
                        break;
                    }

                    case 4:
                    {
                        _ = log.Append(dictionary.TryGetValue(key, out var item) ? item : -1);
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        dictionary.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        _ = dictionary.EnsureCapacity(dictionary.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(dictionary.ContainsKey(key));
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', dictionary.OrderBy(static kvp => kvp.Key)).Append(']');
            }

            return log.ToString();
        },
        static (rng) =>
        {
            var log = new StringBuilder();
            var dictionary = new ValueDictionary<int, int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var key = rng.Next(12);
                var value = rng.Next(20);

                switch (rng.Next(5))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        dictionary[key] = value;
                        break;
                    }

                    case 3:
                    {
                        _ = log.Append(dictionary.Remove(key));
                        break;
                    }

                    case 4:
                    {
                        _ = log.Append(dictionary.TryGetValue(key, out var item) ? item : -1);
                        break;
                    }
                }

                switch (rng.Next(8))
                {
                    case 0:
                    {
                        dictionary.TrimExcess();
                        break;
                    }

                    case 1:
                    {
                        dictionary.EnsureCapacity(dictionary.Count + rng.Next(8));
                        break;
                    }

                    case 2:
                    {
                        _ = log.Append(dictionary.ContainsKey(key));
                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', dictionary.ToArray().OrderBy(static kvp => kvp.Key)).Append(']');
            }

            return log.ToString();
        }, seedCount, seed);
    }

    private static void CompareLinkedList(int seedCount, int seed)
    {
        Compare(static (rng) =>
        {
            var log = new StringBuilder();
            var linkedList = new LinkedList<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);

                switch (rng.Next(7))
                {
                    case 0:
                    case 1:
                    {
                        _ = linkedList.AddFirst(value);
                        break;
                    }

                    case 2:
                    case 3:
                    {
                        _ = linkedList.AddLast(value);
                        break;
                    }

                    case 4:
                    {
                        if (linkedList.Count != 0)
                        {
                            linkedList.RemoveFirst();
                        }
                        break;
                    }

                    case 5:
                    {
                        if (linkedList.Count != 0)
                        {
                            linkedList.RemoveLast();
                        }
                        break;
                    }

                    case 6:
                    {
                        _ = log.Append(linkedList.Remove(value));
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', linkedList).Append(']').Append(linkedList.Count);
            }

            return log.ToString();
        },
        static (rng) =>
        {
            var log = new StringBuilder();
            var linkedList = new ValueLinkedList<int>();

            for (var op = 0; op < OperationCount; op++)
            {
                var value = rng.Next(20);

                switch (rng.Next(7))
                {
                    case 0:
                    case 1:
                    {
                        _ = linkedList.AddFirst(value);
                        break;
                    }

                    case 2:
                    case 3:
                    {
                        _ = linkedList.AddLast(value);
                        break;
                    }

                    case 4:
                    {
                        if (linkedList.Count != 0)
                        {
                            linkedList.RemoveFirst();
                        }
                        break;
                    }

                    case 5:
                    {
                        if (linkedList.Count != 0)
                        {
                            linkedList.RemoveLast();
                        }
                        break;
                    }

                    case 6:
                    {
                        _ = log.Append(linkedList.Remove(value));
                        break;
                    }
                }

                _ = log.Append('[').AppendJoin(',', linkedList.ToArray()).Append(']').Append(linkedList.Count);
            }

            return log.ToString();
        }, seedCount, seed);
    }

    /// <summary>Covers the dictionary at a scale where its backing arrays are repeatedly reallocated.</summary>
    [Test]
    public static void LargeDictionaryTest() => CompareLargeDictionary(20000, QuickSeed);

    [Test]
    [Explicit]
    [Category("OuterLoop")]
    public static void LargeDictionaryExhaustiveTest() => CompareLargeDictionary(500000, Random.Shared.Next());

    private static void CompareLargeDictionary(int operationCount, int seedSourceSeed)
    {
        var oracle = new Dictionary<int, int>();
        var actual = new ValueDictionary<int, int>();

        var seed = s_replaySeed ?? new Random(seedSourceSeed).Next();
        var hint = SeedHint(seed);
        var rng = new Random(seed);

        for (var op = 0; op < operationCount; op++)
        {
            var key = rng.Next(4000);
            var value = rng.Next();

            if (rng.Next(4) == 0)
            {
                Assert.That(actual.Remove(key), Is.EqualTo(oracle.Remove(key)), $"{hint}, op {op}");
            }
            else
            {
                oracle[key] = value;
                actual[key] = value;
            }

            Assert.That(actual.Count, Is.EqualTo(oracle.Count), $"{hint}, op {op}");
        }

        foreach (var keyValuePair in oracle)
        {
            Assert.That(actual.TryGetValue(keyValuePair.Key, out var value), Is.True, hint);
            Assert.That(value, Is.EqualTo(keyValuePair.Value), hint);
        }
    }

    /// <summary>Covers the queue at a scale where its backing array is repeatedly reallocated.</summary>
    [Test]
    public static void LargeQueueTest() => CompareLargeQueue(20000, QuickSeed);

    [Test]
    [Explicit]
    [Category("OuterLoop")]
    public static void LargeQueueExhaustiveTest() => CompareLargeQueue(500000, Random.Shared.Next());

    private static void CompareLargeQueue(int operationCount, int seedSourceSeed)
    {
        var oracle = new Queue<int>();
        var actual = new ValueQueue<int>();

        var seed = s_replaySeed ?? new Random(seedSourceSeed).Next();
        var hint = SeedHint(seed);
        var rng = new Random(seed);

        for (var op = 0; op < operationCount; op++)
        {
            if (rng.Next(4) == 0)
            {
                Assert.That(actual.TryDequeue(out var actualItem), Is.EqualTo(oracle.TryDequeue(out var oracleItem)), $"{hint}, op {op}");
                Assert.That(actualItem, Is.EqualTo(oracleItem), $"{hint}, op {op}");
            }
            else
            {
                var value = rng.Next();

                oracle.Enqueue(value);
                actual.Enqueue(value);
            }

            Assert.That(actual.Count, Is.EqualTo(oracle.Count), $"{hint}, op {op}");
        }

        Assert.That(actual.ToArray(), Is.EqualTo(oracle.ToArray()), hint);
    }

    /// <summary>Covers the stack at a scale where its backing array is repeatedly reallocated.</summary>
    [Test]
    public static void LargeStackTest() => CompareLargeStack(20000, QuickSeed);

    [Test]
    [Explicit]
    [Category("OuterLoop")]
    public static void LargeStackExhaustiveTest() => CompareLargeStack(500000, Random.Shared.Next());

    private static void CompareLargeStack(int operationCount, int seedSourceSeed)
    {
        var oracle = new Stack<int>();
        var actual = new ValueStack<int>();

        var seed = s_replaySeed ?? new Random(seedSourceSeed).Next();
        var hint = SeedHint(seed);
        var rng = new Random(seed);

        for (var op = 0; op < operationCount; op++)
        {
            if (rng.Next(4) == 0)
            {
                Assert.That(actual.TryPop(out var actualItem), Is.EqualTo(oracle.TryPop(out var oracleItem)), $"{hint}, op {op}");
                Assert.That(actualItem, Is.EqualTo(oracleItem), $"{hint}, op {op}");
            }
            else
            {
                var value = rng.Next();

                oracle.Push(value);
                actual.Push(value);
            }

            Assert.That(actual.Count, Is.EqualTo(oracle.Count), $"{hint}, op {op}");
        }

        Assert.That(actual.ToArray(), Is.EqualTo(oracle.ToArray()), hint);
    }

    /// <summary>Covers the list at a scale where its backing array is repeatedly reallocated.</summary>
    [Test]
    public static void LargeListTest() => CompareLargeList(20000, QuickSeed);

    [Test]
    [Explicit]
    [Category("OuterLoop")]
    public static void LargeListExhaustiveTest() => CompareLargeList(500000, Random.Shared.Next());

    private static void CompareLargeList(int operationCount, int seedSourceSeed)
    {
        var oracle = new List<int>();
        var actual = new ValueList<int>();

        var seed = s_replaySeed ?? new Random(seedSourceSeed).Next();
        var hint = SeedHint(seed);
        var rng = new Random(seed);

        for (var op = 0; op < operationCount; op++)
        {
            var value = rng.Next();

            if ((rng.Next(4) == 0) && (oracle.Count != 0))
            {
                var index = rng.Next(oracle.Count);

                oracle.RemoveAt(index);
                actual.RemoveAt(index);
            }
            else
            {
                oracle.Add(value);
                actual.Add(value);
            }

            Assert.That(actual.Count, Is.EqualTo(oracle.Count), $"{hint}, op {op}");
        }

        Assert.That(actual.ToArray(), Is.EqualTo(oracle.ToArray()), hint);
    }

    /// <summary>Covers the linked list at a scale where its nodes are repeatedly recycled.</summary>
    [Test]
    public static void LargeLinkedListTest() => CompareLargeLinkedList(20000, QuickSeed);

    [Test]
    [Explicit]
    [Category("OuterLoop")]
    public static void LargeLinkedListExhaustiveTest() => CompareLargeLinkedList(500000, Random.Shared.Next());

    private static void CompareLargeLinkedList(int operationCount, int seedSourceSeed)
    {
        var oracle = new LinkedList<int>();
        var actual = new ValueLinkedList<int>();

        var seed = s_replaySeed ?? new Random(seedSourceSeed).Next();
        var hint = SeedHint(seed);
        var rng = new Random(seed);

        for (var op = 0; op < operationCount; op++)
        {
            var value = rng.Next();

            switch (rng.Next(4))
            {
                case 0:
                {
                    if (oracle.Count != 0)
                    {
                        oracle.RemoveFirst();
                        actual.RemoveFirst();
                    }
                    break;
                }

                case 1:
                {
                    _ = oracle.AddFirst(value);
                    _ = actual.AddFirst(value);
                    break;
                }

                default:
                {
                    _ = oracle.AddLast(value);
                    _ = actual.AddLast(value);
                    break;
                }
            }

            Assert.That(actual.Count, Is.EqualTo(oracle.Count), $"{hint}, op {op}");
        }

        Assert.That(actual.ToArray(), Is.EqualTo(oracle.ToArray()), hint);
    }
}
