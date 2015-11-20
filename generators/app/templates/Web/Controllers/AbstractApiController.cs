using <%= solutionName %>Web.Utility;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;

namespace <%= solutionName %>Web.Controllers
{
    public abstract class AbstractApiController : ApiController
    {
      protected void Execute(Action action)
      {
          try
          {
              Logger<AbstractApiController>.Debug("Execute an action {0}.{1}",action.Target.ToString(), action.Method.Name);
              action();
              Logger<AbstractApiController>.Debug("Execution completed {0}.{1}", action.Target.ToString(), action.Method.Name);
          }
          catch (Exception exception)
          {
              Logger<AbstractApiController>.Handle(exception);
              throw new Exception();
          }
      }

      protected TResult Execute<TResult>(Func<TResult> function)
      {
          var result = default(TResult);
          try
          {
              Logger<AbstractApiController>.Debug("Execute a function {0}.{1}", function.Target.ToString(), function.Method.Name);
              result = function();
              Logger<AbstractApiController>.Debug("Execution completed {0}.{1}", function.Target.ToString(), function.Method.Name);
          }
          catch (Exception exception)
          {

              Logger<AbstractApiController>.Handle(exception);
              throw new Exception();
          }
          return result;
      }
      
    }
}
