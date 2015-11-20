using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace <%= solutionName %>Web.Utility
{
    public static class Logger<TType>
    {
        public static void Handle(Exception exception)
        {
            var log = log4net.LogManager.GetLogger(typeof(TType));
            log.Error(exception.Message, exception);
        }

        public static void Debug(string message)
        {
            var log = log4net.LogManager.GetLogger(typeof(TType));
            log.Debug(message);
        }

        public static void Debug(string messageTemplate, params string[] arguments)
        {
            Debug(string.Format(messageTemplate, arguments));
        }

    }
}
