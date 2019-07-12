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
      (new Server()).Start(/* PORT */8080);
    }
  }

  [Controller]
  class ItemController {
    [Autowired]
    public ItemService Svc { set; get; }

    [UrlPattern("/item/list")]
    public void List(HttpListenerContext context) {
      ServerUtil.WriteResponseText(context, ServerUtil.ToJson(Svc.GetList()));
    }
  }

  [Service]
  class ItemService { 
    [Autowired]
    public RandomUtil Util { set; get; }

    public List<string> GetList() {
      return Util.CreateRandomItemList(10);
    }
  }

  [Component]
  class RandomUtil {
    public List<string> CreateRandomItemList(int n) {
      List<string> itemList = new List<string>();
      System.Random rnd = new System.Random();
      for(int i = 0; i < n; i++) {
        itemList.Add(String.Format("Item{0}", rnd.Next(100)));
      }

      return  itemList;
    }
  }
}

