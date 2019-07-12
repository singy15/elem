/*
 * elem, the elemental and primitive web application server with basic DI.
 */

using System;
using System.Reflection;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Json;

namespace Elem {
  [AttributeUsage(AttributeTargets.Class)]
  class Component : Attribute { }

  [AttributeUsage(AttributeTargets.Class)]
  class Controller : Component { }

  [AttributeUsage(AttributeTargets.Class)]
  class Service : Component { }

  [AttributeUsage(AttributeTargets.Property)]
  class Autowired : Attribute { }

  class Context {
    private HashSet<Type> types = new HashSet<Type>();
    private Dictionary<Type, object> pool = new Dictionary<Type, object>();

    public Context() {
      Assembly
        .GetExecutingAssembly()
        .GetTypes()
        .Where(x => (null != x.GetCustomAttribute(typeof(Component))))
        .ToList()
        .ForEach(x => types.Add(x));
    }

    public object GetBean(Type type) {
      if(!types.Contains(type)) {
        throw new Exception("Type not found : " + type.Name);
      }

      if(pool.ContainsKey(type)) {
        return pool[type];
      } else {
        object obj = CreateInstance(type);
        pool.Add(type, obj);
        return obj;
      }
    }

    public object CreateInstance(Type type) {
      object obj = (object)Activator.CreateInstance(type);

      type
        .GetProperties(
          BindingFlags.InvokeMethod 
          | BindingFlags.Public 
          | BindingFlags.NonPublic 
          | BindingFlags.Instance)
        .Where(f => (null != f.GetCustomAttribute(typeof(Autowired))))
        .ToList()
        .ForEach(f => f.SetValue(obj, GetBean(f.PropertyType)));
            
      return obj;
    }
  }

  class Server {
    private const string ROOT_URL_BASE = "http://*:{0}/";
    private const string STATIC_ROOT = "./public/";
    private string rootUrl;
    private List<object> controllers;

    public Server() {
      Context ctx = new Context();

      this.controllers = Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(x => (null != x.GetCustomAttribute(typeof(Controller))))
        .Select(x => ctx.GetBean(x))
        .ToList();
    }

    public void Start(int port) {
      Console.WriteLine( "*** elem started on port {0} ***", port);
      Console.WriteLine( "Press Ctrl+C to stop.");

      this.rootUrl = String.Format(ROOT_URL_BASE, port.ToString());

      try {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(rootUrl);
        listener.Start();
        while (true) {
          Routing(listener.GetContext());
        }
      }
      catch (Exception ex) {
        Console.WriteLine("Error: " + ex.Message);
      }
    }

    public void Routing(HttpListenerContext context) {
      string url = context.Request.Url.ToString();
      string localPath = context.Request.Url.LocalPath.ToString();

      context.Response.StatusCode = 200;

      var ctrlMethod = controllers
        .SelectMany(c => c.GetType()
                          .GetMethods( BindingFlags.Public 
                                       | BindingFlags.Instance 
                                       | BindingFlags.DeclaredOnly),
                    (value, m) => new { Ctrl = value, MethodInfo = m })
        .Where(x => 
            (null != x.MethodInfo.GetCustomAttribute(typeof(UrlPattern)))
            && Regex.IsMatch(localPath, 
                ((UrlPattern)x.MethodInfo.GetCustomAttribute(
                        typeof(UrlPattern))).Pattern))
        .FirstOrDefault();

      if(null != ctrlMethod) {
        ctrlMethod.MethodInfo.Invoke(ctrlMethod.Ctrl, new object[] { context });
      } else {
        string path = STATIC_ROOT 
          + url.Substring(rootUrl.Length, url.Length - rootUrl.Length);
        if(File.Exists(path)) {
          ServerUtil.WriteResponseBytes(context, File.ReadAllBytes(path));
        } else {
          context.Response.StatusCode = 404;
          ServerUtil.WriteResponseText(context, "404 not found!");
        }
      }
      context.Response.Close();
    }
  }

  class ServerUtil {
    public static void WriteResponseText(
        HttpListenerContext context, string text) {
      byte[] content = Encoding.UTF8.GetBytes(text);
      context.Response.OutputStream.Write(content, 0, content.Length);
    }

    public static void WriteResponseBytes(
        HttpListenerContext context, byte[] bytes) {
      context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public static string ToJson(object obj) {
      using (var ms = new MemoryStream())
      using (var sr = new StreamReader(ms)) {
        (new DataContractJsonSerializer(obj.GetType())).WriteObject(ms, obj);
        ms.Position = 0;
        return sr.ReadToEnd();
      }
    }
  }

  [AttributeUsage(AttributeTargets.Method)]
  class UrlPattern : Attribute {
    public string Pattern { get; set; }
    public UrlPattern(string pattern) {
      Pattern = pattern;
    }
  }
}

