using System;

namespace fluent_api_migrator.Exceptions
{
    internal class EdmxFileException : Exception
    {
        public EdmxFileException()
        { }

        public EdmxFileException(string message) : base(message)
        { }

        public EdmxFileException(string message, Exception ex) : base(message, ex)
        { }
    }
}
