using System;

namespace MyExtension.Impl
{
    /// <summary>An expected failure: shown to the user without a stack trace.</summary>
    public sealed class UserException : Exception
    {
        public UserException(string message) : base(message) { }
    }
}
