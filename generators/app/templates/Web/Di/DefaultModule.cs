using Ninject.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace <%= solutionName %>Web.Di
{
    /// <summary>
    /// Describes the type bindings that are used during dependency injection.
    /// </summary>
    public class DefaultModule: NinjectModule
    {
        public override void Load()
        {
            // Bind<IService>().To<Service>();
        }
    }
}
