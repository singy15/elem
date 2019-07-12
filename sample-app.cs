/*
 * Sample application
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

namespace SampleApp {
  using Elem;

  class Program {
    public static void Main(string[] args) {
      WebContext ctx = new WebContext("./context.xml");
      ((Server)ctx.GetBean(typeof(Server))).Start(/* PORT */8080);
    }
  }

  [Controller]
  class ItemController {
    [Autowired]
    public IItemService Svc { set; get; }

    [UrlPattern("/item/list")]
    public void List(HttpListenerContext context) {
      ServerUtil.WriteResponseText(context, ServerUtil.ToJson(Svc.GetList()));
    }
  }

  [Controller]
  class PersonController {
    [Autowired]
    public IPersonService Svc { set; get; }

    [UrlPattern("/person/list")]
    public void List(HttpListenerContext context) {
      ServerUtil.WriteResponseText(context, ServerUtil.ToJson(Svc.GetList()));
    }
  }

  interface IItemService {
    List<string> GetList();
  }

  [Service]
  class ItemService : IItemService { 
    [Autowired]
    public RandomUtil Util { set; get; }

    public List<string> GetList() {
      return Util.CreateRandomItemList("item", 10);
    }
  }

  interface IPersonService {
    List<string> GetList();
  }

  [Service]
  class PersonService : IPersonService { 
    [Autowired]
    public RandomUtil Util { set; get; }

    public List<string> GetList() {
      throw new Exception("not implemented");
    }
  }

  [Service]
  class MockPersonService : IPersonService { 
    public List<string> GetList() {
      List<string> ls = new List<string>();
      ls.Add("dummy person 1");
      ls.Add("dummy person 2");
      return ls;
    }
  }

  [Component]
  class RandomUtil {
    public List<string> CreateRandomItemList(string prefix, int n) {
      List<string> itemList = new List<string>();
      System.Random rnd = new System.Random();
      for(int i = 0; i < n; i++) {
        itemList.Add(String.Format("{0}{1}", prefix, rnd.Next(100)));
      }

      return  itemList;
    }
  }
}

