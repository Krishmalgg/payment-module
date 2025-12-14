using System;

namespace PaymentModule.Domain.Exceptions
{
    public class SecurityTamperingException : Exception
    {
        public SecurityTamperingException() : base("Security tampering detected.") { }
        public SecurityTamperingException(string message) : base(message) { }
        public SecurityTamperingException(string message, Exception innerException) : base(message, innerException) { }
    }
}