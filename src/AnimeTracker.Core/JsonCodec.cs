using System;
using System.Collections.Generic;
#if MODERN_DOTNET
using System.Text.Json;
#else
using System.Web.Script.Serialization;
#endif
namespace AnimeTracker {
public sealed class JsonCodec {
#if MODERN_DOTNET
 public string Serialize(object value){return JsonSerializer.Serialize(value,new JsonSerializerOptions{IncludeFields=true});}
 public object DeserializeObject(string text){using(var document=JsonDocument.Parse(text)){return ConvertValue(document.RootElement);}}
 static object ConvertValue(JsonElement element){
  switch(element.ValueKind){
   case JsonValueKind.Object:var map=new Dictionary<string,object>();foreach(var p in element.EnumerateObject())map[p.Name]=ConvertValue(p.Value);return map;
   case JsonValueKind.Array:var list=new List<object>();foreach(var item in element.EnumerateArray())list.Add(ConvertValue(item));return list.ToArray();
   case JsonValueKind.String:return element.GetString();
   case JsonValueKind.Number:int number;if(element.TryGetInt32(out number))return number;return element.GetDouble();
   case JsonValueKind.True:return true;case JsonValueKind.False:return false;default:return null;
  }
 }
#else
 readonly JavaScriptSerializer codec=new JavaScriptSerializer();
 public string Serialize(object value){return codec.Serialize(value);}
 public object DeserializeObject(string text){return codec.DeserializeObject(text);}
#endif
}
}
