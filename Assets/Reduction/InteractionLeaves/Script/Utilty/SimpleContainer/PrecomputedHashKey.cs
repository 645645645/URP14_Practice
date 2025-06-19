using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Collections
{
    public readonly struct PrecomputedHashKey :
        IEquatable<int>,
        IEquatable<PrecomputedHashKey>,
        IComparable<int>,
        IComparable<PrecomputedHashKey>
    {
        public readonly int HashCode;

        public PrecomputedHashKey(int value)
        {
            HashCode = value.GetHashCode();
        }

        public bool Equals(int other)
        {
            return HashCode.Equals(other);
        }

        public bool Equals(PrecomputedHashKey other)
        {
            return HashCode.Equals(other.HashCode);
        }

        public override int GetHashCode()
        {
            return HashCode;
        }

        public int CompareTo(int other)
        {
            return HashCode.CompareTo(other);
        }

        public int CompareTo(PrecomputedHashKey other)
        {
            return HashCode.CompareTo(other.HashCode);
        }

        public static implicit operator PrecomputedHashKey(int value)
        {
            return new PrecomputedHashKey(value);
        }
    }
}