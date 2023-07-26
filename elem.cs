/*
 * elem, the elemental and primitive web application server with basic DI.
 */

//// Add following references
// - System.Runtime.Serialization
// - System.Text.Json 

using System;
using System.Reflection;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Json;
using System.Xml.Linq;
using System.Web;
using System.Text.Json;

namespace Elem
{
    public enum RouteMethod
    {
        GET, PUT, POST, DELETE, OTHER
    }

    [AttributeUsage(AttributeTargets.Class)]
    class Component : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    class Controller : Component { }

    [AttributeUsage(AttributeTargets.Class)]
    class Service : Component { }

    [AttributeUsage(AttributeTargets.Class)]
    class Controllers : Component { }

    [AttributeUsage(AttributeTargets.Property)]
    class Autowired : Attribute
    {
        public string Qualifier { get; set; }

        public Autowired(string qualifier)
        {
            Qualifier = qualifier;
        }

        public Autowired()
        {
            Qualifier = null;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    class AutowiredGroup : Attribute
    {
        public string GroupName { get; set; }

        public AutowiredGroup(string groupName)
        {
            GroupName = groupName;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    class RequestBody : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    class RequestJson : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    class UriParam : Attribute { }

    class ReflectionUtil
    {
        public static List<MethodInfo> GetFilteredMethods(Type type, BindingFlags flags, List<Type> attributes = null)
        {
            IEnumerable<MethodInfo> ls = type.GetMethods(flags).ToList();
            foreach (var attr in attributes) {
                ls = ls.Where(x => (null != x.GetCustomAttribute(attr)));
            }
            return ls.ToList();
        }
    }

    class Context
    {
        protected const BindingFlags INJECTION_TARGET =
          BindingFlags.InvokeMethod
          | BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance;
        private Dictionary<Type, object> pool = new Dictionary<Type, object>();
        private Dictionary<string, Type> beanDef = new Dictionary<string, Type>();
        private Dictionary<string, object> beanPool =
          new Dictionary<string, object>();
        protected HashSet<Type> types = new HashSet<Type>();
        protected Dictionary<string, List<string>> beanDefGroup =
          new Dictionary<string, List<string>>();
        public bool AutowireSingleImpl { get; set; }

        public Context()
        {
            Initialize();
        }

        public Context(string contextPath)
        {
            XDocument xml = XDocument.Load(contextPath);
            XElement configuration = xml.Element("configuration");
            var beans = configuration.Elements("beans").Elements("bean");
            foreach (XElement bean in beans)
            {
                string id = bean.Attribute("id").Value;
                string type = bean.Attribute("type").Value;

                if (beanDef.ContainsKey(id))
                {
                    throw new Exception("Duplicate bean definition : " + id);
                }

                Type cand = Assembly
                  .GetExecutingAssembly()
                  .GetTypes()
                  .Where(x => (x.FullName == type))
                  .SingleOrDefault();

                if (null == cand)
                {
                    throw new Exception("Bean candidate not found : " + type);
                }

                beanDef.Add(id, cand);
            }

            Initialize();
        }

        public void Initialize()
        {
            AutowireSingleImpl = true;

            Assembly
              .GetExecutingAssembly()
              .GetTypes()
              .Where(x => (null != x.GetCustomAttribute(typeof(Component))))
              .ToList()
              .ForEach(x =>
              {
                  if (!beanDef.ContainsKey(x.FullName))
                  {
                      beanDef.Add(x.FullName, x);
                  }
              });
        }

        public virtual object CreateInstance(string beanName)
        {
            object obj = (object)Activator.CreateInstance(beanDef[beanName]);
            InjectBean(obj);
            InjectBeanGroup(obj);
            return obj;
        }

        public void InjectBean(object obj)
        {
            obj
             .GetType()
             .GetProperties(INJECTION_TARGET)
             .Where(f => (null != f.GetCustomAttribute(typeof(Autowired))))
             .ToList()
             .ForEach(prop =>
             {
                 Autowired autowired =
              (Autowired)(prop.GetCustomAttribute(typeof(Autowired)));

                 if (null != autowired.Qualifier)
                 {
                     prop.SetValue(obj, GetBean(autowired.Qualifier));
                 }
                 else if (!prop.PropertyType.IsInterface)
                 {
                     prop.SetValue(obj, GetBean(prop.PropertyType.FullName));
                 }
                 else
                 {
                     if (beanDef.ContainsKey(prop.PropertyType.FullName))
                     {
                         prop.SetValue(obj, GetBean(prop.PropertyType.FullName));
                     }
                     else if (AutowireSingleImpl)
                     {
                         List<Type> xs = new List<Type>();
                         foreach (KeyValuePair<string, Type> pair in beanDef)
                         {
                             xs.Add(pair.Value);
                         }
                         IEnumerable<Type> cands = xs
                      .Where(x =>
                          null != x.GetInterface(prop.PropertyType.FullName));

                         if (cands.Count() == 0)
                         {
                             throw new Exception("No compatible bean found : "
                            + prop.PropertyType.FullName);
                         }

                         if (cands.Count() > 1)
                         {
                             throw new Exception("Multiple compatible bean found : "
                            + prop.PropertyType.FullName);
                         }

                         Type cand = cands.Single();

                         prop.SetValue(obj, GetBean(cand.FullName));
                     }
                     else
                     {
                         throw new Exception("Can't inject bean : "
                        + prop.PropertyType.FullName);
                     }
                 }
             });
        }

        public void InjectBeanGroup(object obj)
        {
            obj
              .GetType()
              .GetProperties(INJECTION_TARGET)
              .Where(f => (null != f.GetCustomAttribute(typeof(AutowiredGroup))))
              .ToList()
              .ForEach(prop =>
              {
                  AutowiredGroup autowiredGroup =
              (AutowiredGroup)(prop.GetCustomAttribute(typeof(AutowiredGroup)));

                  if (!beanDefGroup.ContainsKey(autowiredGroup.GroupName))
                  {
                      throw new Exception("Group not found : " + autowiredGroup.GroupName);
                  }

                  List<object> ls = new List<object>();
                  foreach (string bean in beanDefGroup[autowiredGroup.GroupName])
                  {
                      ls.Add(GetBean(bean));
                  }

                  prop.SetValue(obj, ls);
              });
        }

        public object GetBean(Type type)
        {
            return GetBean(type.FullName);
        }

        public object GetBean(string beanName)
        {
            if (!beanDef.ContainsKey(beanName))
            {
                throw new Exception("Bean definition not found : " + beanName);
            }

            if (beanPool.ContainsKey(beanName))
            {
                return beanPool[beanName];
            }
            else
            {
                object obj = CreateInstance(beanName);
                beanPool.Add(beanName, obj);
                return obj;
            }
        }
    }

    class WebContext : Context
    {
        public WebContext() : base()
        {
            DefineControllersGroup();
        }

        public WebContext(string contextPath) : base(contextPath)
        {
            DefineControllersGroup();
        }

        public void DefineControllersGroup()
        {
            beanDefGroup.Add("Elem.Controllers", Assembly.GetExecutingAssembly()
              .GetTypes()
              .Where(x => (null != x.GetCustomAttribute(typeof(Controller))))
              .Select(x => x.FullName)
              .ToList());
        }
    }

    [Component]
    class Server
    {
        private const string ROOT_URL_BASE =
          "http://localhost:{0}/"; // "http://*:{0}/";
        private const string STATIC_ROOT = "./public/";
        private string rootUrl;

        [AutowiredGroup("Elem.Controllers")]
        public List<object> Controllers { get; set; }

        public JsonSerializerOptions JsonSerializerOptions { get; set; }

        public Server()
        {
            this.JsonSerializerOptions = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                // WriteIndented = true
            };
        }

        public void Start(int port)
        {
            Console.WriteLine("*** elem started on port {0} ***", port);
            Console.WriteLine("Press Ctrl+C to stop.");

            this.rootUrl = String.Format(ROOT_URL_BASE, port.ToString());

            try
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add(rootUrl);
                listener.Start();
                while (true)
                {
                    ResolveRouting(listener.GetContext());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("*** Error ***");
                Console.WriteLine("message: " + ex.Message);
                Console.WriteLine("stack trace: ");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                throw ex;
            }
        }

        public RouteMethod StringToRouteMethod(string methodName)
        {
            switch (methodName)
            {
                case "GET":
                    return RouteMethod.GET;
                case "POST":
                    return RouteMethod.POST;
                case "PUT":
                    return RouteMethod.PUT;
                case "DELETE":
                    return RouteMethod.DELETE;
                default:
                    return RouteMethod.OTHER;
            }
        }

        public string ConvertParameterizedUriToRegexPattern(string parameterizedUri)
        {
            var matches = Regex.Matches(parameterizedUri, "{(?<name>.+?)}");
            var regexPattern = parameterizedUri;
            foreach (Match m in matches)
            {
                string name = m.Groups["name"].Value;
                regexPattern = regexPattern.Replace("{" + name + "}", "(?<" + name + ">.+?)");
            }
            return "^" + regexPattern + "$";
        }

        public void ResolveRouting(HttpListenerContext context)
        {
            string url = context.Request.Url.ToString();
            string localPath = context.Request.Url.LocalPath.ToString();
            var uri = new Uri(url);
            var queryParam = HttpUtility.ParseQueryString(uri.Query);
            var httpMethod = context.Request.HttpMethod;

            context.Response.StatusCode = 200;

            var ctrlMethod = Controllers
              .SelectMany(c => c.GetType()
                                .GetMethods(BindingFlags.Public
                                             | BindingFlags.Instance
                                             | BindingFlags.DeclaredOnly),
                          (value, m) => new { Ctrl = value, MethodInfo = m })
              .Where(x =>
                  (null != x.MethodInfo.GetCustomAttribute(typeof(Routing)))
                  && (StringToRouteMethod(httpMethod)
                    == ((Routing)x.MethodInfo.GetCustomAttribute(typeof(Routing))).Method))
              .Where(x =>
                  (null != x.MethodInfo.GetCustomAttribute(typeof(Routing)))
                  && Regex.IsMatch(localPath,
                      ConvertParameterizedUriToRegexPattern(((Routing)x.MethodInfo.GetCustomAttribute(
                              typeof(Routing))).Pattern)))
              .FirstOrDefault();

            // Console.WriteLine(localPath);
            // Console.WriteLine(ConvertParameterizedUriToRegexPattern("/item/list/{id}/{id2}"));
            // var regex = new Regex(ConvertParameterizedUriToRegexPattern("/item/list/{id}/{id2}"));
            // Console.WriteLine(regex.IsMatch(localPath));
            // var mc = regex.Match(localPath);
            // foreach(var g in mc.Groups.Keys) {
            //   Console.WriteLine(g + "=" + mc.Groups[g]);
            // }

            if (null != ctrlMethod)
            {
                var attrRouting = (Routing)ctrlMethod.MethodInfo.GetCustomAttribute(typeof(Routing));

                // Call method when a route found
                try
                {
                    Match match = (new Regex(ConvertParameterizedUriToRegexPattern(attrRouting.Pattern))).Match(localPath);

                    List<object> parameters = new List<object>();

                    foreach (var p in ctrlMethod.MethodInfo.GetParameters())
                    {

                        var request = context.Request;
                        RequestBody attrRequestBody = (RequestBody)Attribute.GetCustomAttribute(p, typeof(RequestBody));
                        RequestJson attrRequestJson = (RequestJson)Attribute.GetCustomAttribute(p, typeof(RequestJson));
                        UriParam attrUriParam = (UriParam)Attribute.GetCustomAttribute(p, typeof(UriParam));

                        if (null != attrRequestBody)
                        {
                            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                            {
                                string body = reader.ReadToEnd();
                                parameters.Add(body);
                            }
                            continue;
                        }
                        else if (null != attrRequestJson)
                        {
                            parameters.Add(System.Text.Json.JsonSerializer.Deserialize(request.InputStream, p.ParameterType, this.JsonSerializerOptions));
                            continue;
                        }
                        else if (p.ParameterType == typeof(HttpListenerContext))
                        {
                            parameters.Add(context);
                            continue;
                        }

                        string paramValue = null;
                        if (null != attrUriParam)
                        {
                            paramValue = match.Groups[p.Name].Value;
                        }
                        else
                        {
                            paramValue = queryParam.Get(p.Name);
                        }

                        if (p.ParameterType == typeof(Int32))
                        {
                            parameters.Add(Int32.Parse(paramValue));
                            continue;
                        }
                        else if (p.ParameterType == typeof(Int64))
                        {
                            parameters.Add(Int64.Parse(paramValue));
                            continue;
                        }
                        else if (p.ParameterType == typeof(Single))
                        {
                            parameters.Add(Single.Parse(paramValue));
                            continue;
                        }
                        else if (p.ParameterType == typeof(Double))
                        {
                            parameters.Add(Double.Parse(paramValue));
                            continue;
                        }
                        else if (p.ParameterType == typeof(Boolean))
                        {
                            parameters.Add(Boolean.Parse(paramValue));
                            continue;
                        }
                        else if (p.ParameterType == typeof(Decimal))
                        {
                            parameters.Add(Decimal.Parse(paramValue));
                            continue;
                        }
                        else if (p.ParameterType == typeof(string))
                        {
                            parameters.Add(paramValue);
                            continue;
                        }

                        throw new Exception("Unsupported controller method parameter type."
                            + " Class=" + ctrlMethod.MethodInfo.DeclaringType.Name + "." + ctrlMethod.MethodInfo.Name
                            + " ParameterType=" + p.ParameterType.Name);
                    }

                    List<object> additionalCallParameters = new List<object>();
                    additionalCallParameters.Add(context);

                    var beforeRun = ReflectionUtil.GetFilteredMethods(ctrlMethod.Ctrl.GetType(),
                          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy,
                          new List<Type>() { typeof(BeforeRun) }).FirstOrDefault();

                    if (null != beforeRun)
                    {
                        beforeRun.Invoke(ctrlMethod.Ctrl, additionalCallParameters.ToArray());
                    }

                    if ((null != ctrlMethod.MethodInfo.GetCustomAttribute(typeof(AllowCors)))
                        || (null != ctrlMethod.Ctrl.GetType().GetCustomAttribute(typeof(AllowCors))))
                    {
                        //if (context.Request.HttpMethod == "OPTIONS")
                        //{
                        //    ServerUtil.AddResponseHeader(context, "Access-Control-Allow-Headers", "Content-Type, Accept, X-Requested-With");
                        //    ServerUtil.AddResponseHeader(context, "Access-Control-Allow-Methods", "GET, POST");
                        //    ServerUtil.AddResponseHeader(context, "Access-Control-Max-Age", "1728000");
                        //}
                        ServerUtil.AddResponseHeader(context, "Access-Control-Allow-Origin", "*");
                    }

                    ctrlMethod.MethodInfo.Invoke(ctrlMethod.Ctrl, parameters.ToArray());

                    var afterRun = ReflectionUtil.GetFilteredMethods(ctrlMethod.Ctrl.GetType(),
                          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy,
                          new List<Type>() { typeof(BeforeRun) }).FirstOrDefault();

                    if (null != afterRun)
                    {
                        afterRun.Invoke(ctrlMethod.Ctrl, additionalCallParameters.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    // error 500
                    context.Response.StatusCode = 500;
                    ServerUtil.WriteResponseText(context, "500 Internal Server Error");
                    Console.WriteLine("*** Internal Server Error ***");
                    Console.WriteLine("message: " + ex.Message);
                    Console.WriteLine("stack trace: ");
                    Console.WriteLine(ex.StackTrace);
                    Console.WriteLine();
                }
            }
            else
            {
                // Try fetch static resource
                string path = STATIC_ROOT
                  + url.Substring(rootUrl.Length, url.Length - rootUrl.Length);
                if (File.Exists(path))
                {
                    // Return static resource
                    ServerUtil.WriteResponseBytes(context, File.ReadAllBytes(path));
                }
                else
                {
                    // error 404
                    context.Response.StatusCode = 404;
                    ServerUtil.WriteResponseText(context, "404 not found!");
                }
            }
            context.Response.Close();
        }
    }

    class ServerUtil
    {
        public static JsonSerializerOptions JsonSerializerOptions { get; set; }

        static ServerUtil()
        {
            JsonSerializerOptions = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                // WriteIndented = true
            };
        }

        public static void AddResponseHeader(
            HttpListenerContext context, string key, string value)
        {
            context.Response.Headers.Add(key + ":" + value);
        }

        public static void WriteResponseText(
            HttpListenerContext context, string text)
        {
            byte[] content = Encoding.UTF8.GetBytes(text);
            context.Response.OutputStream.Write(content, 0, content.Length);
        }

        public static void WriteResponseBytes(
            HttpListenerContext context, byte[] bytes)
        {
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        public static string ToJson(object obj)
        {
            // using (var ms = new MemoryStream())
            // using (var sr = new StreamReader(ms)) {
            //   (new DataContractJsonSerializer(obj.GetType())).WriteObject(ms, obj);
            //   ms.Position = 0;
            //   return sr.ReadToEnd();
            // }
            return System.Text.Json.JsonSerializer.Serialize(obj, JsonSerializerOptions);
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    class Routing : Attribute
    {
        public string Pattern { get; set; }
        public RouteMethod Method { get; set; }
        public Routing(string pattern, RouteMethod method = RouteMethod.GET)
        {
            Pattern = pattern;
            Method = method;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    class BeforeRun : Attribute
    {
        public BeforeRun()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    class AfterRun : Attribute
    {
        public AfterRun()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    class AllowCors : Attribute
    {
        public AllowCors()
        {
        }
    }

    public class CorsBaseController
    {
        [BeforeRun]
        public void AddHeaders(HttpListenerContext context)
        {
            //if (context.Request.HttpMethod == "OPTIONS")
            //{
            //    ServerUtil.AddResponseHeader(context, "Access-Control-Allow-Headers", "Content-Type, Accept, X-Requested-With");
            //    ServerUtil.AddResponseHeader(context, "Access-Control-Allow-Methods", "GET, POST");
            //    ServerUtil.AddResponseHeader(context, "Access-Control-Max-Age", "1728000");
            //}
            ServerUtil.AddResponseHeader(context, "Access-Control-Allow-Origin", "*");
        }
    }

}

