using System;
using System.Collections.Generic;

namespace PaymentModule.Domain.ValueObjects
{
    public class EncryptedData : IEquatable<EncryptedData>
    {
        public string Value { get; }

        public EncryptedData(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Encrypted data cannot be empty", nameof(value));
            
            Value = value;
        }

        public static implicit operator string(EncryptedData encryptedData) => encryptedData.Value;
        public static explicit operator EncryptedData(string value) => new EncryptedData(value);

        public override bool Equals(object? obj)
        {
            return Equals(obj as EncryptedData);
        }

        public bool Equals(EncryptedData? other)
        {
            return other is not null && Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }
    }
}