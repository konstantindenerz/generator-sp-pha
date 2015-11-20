using <%= solutionName %>Web.Utility;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace <%= solutionName %>Web.Controllers
{
    public abstract class AbstractController : Controller
    {
        protected void Execute(Action action)
        {
            try
            {
                Logger<AbstractController>.Debug("Execute an action {0}.{1}", action.Target.ToString(), action.Method.Name);
                action();
                Logger<AbstractController>.Debug("Execution completed {0}.{1}", action.Target.ToString(), action.Method.Name);
            }
            catch (Exception exception)
            {
                Response.StatusCode = 500;
                Logger<AbstractController>.Handle(exception);
            }
        }

        protected TResult Execute<TResult>(Func<TResult> function)
        {
            var result = default(TResult);
            try
            {
                Logger<AbstractController>.Debug("Execute an function {0}.{1}", function.Target.ToString(), function.Method.Name);
                result = function();
                Logger<AbstractController>.Debug("Execution completed {0}.{1}", function.Target.ToString(), function.Method.Name);
            }
            catch (Exception exception)
            {
                Response.StatusCode = 500;
                Logger<AbstractController>.Handle(exception);
            }
            return result;
        }
    }
}
