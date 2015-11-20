using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using <%= solutionName %>Web.App_Start;

namespace <%= solutionName %>Web
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            WebApiConfig.Register(GlobalConfiguration.Configuration);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            InitializeLog4Net();
        }

        private void InitializeLog4Net()
        {
            string configPath = Server.MapPath("~/log4net.config");
            if (File.Exists(configPath))
            {
                log4net.Config.XmlConfigurator.ConfigureAndWatch(new FileInfo(configPath));
            }
        }
    }
}
