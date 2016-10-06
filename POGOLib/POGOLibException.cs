using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace POGOLib
{
    public class POGOLibException : Exception
    {
        public POGOLibException()
            : base() { }

        public POGOLibException(string message)
            : base(message) { }

        public POGOLibException(string format, params object[] args)
            : base(string.Format(format, args)) { }

        public POGOLibException(string message, Exception innerException)
            : base(message, innerException) { }

        public POGOLibException(string format, Exception innerException, params object[] args)
            : base(string.Format(format, args), innerException) { }
    }
}