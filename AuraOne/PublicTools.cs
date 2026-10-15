using System.Text.Json;
namespace AuraOne;
public static class PublicTools
{
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(20)};
    static PublicTools(){Http.DefaultRequestHeaders.UserAgent.ParseAdd("AuraOne/1.0 (personal voice assistant)");}
    static async Task<JsonElement> Get(string url){using var response=await Http.GetAsync(url);response.EnsureSuccessStatusCode();using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();}
    public static async Task<object> Run(string name,JsonElement args,Brain brain)
    {
        string S(string key)=>args.TryGetProperty(key,out var value)?value.GetString()??"":"";
        if(name=="calculator"&&brain.Enabled("calculator"))return new{result=Calculator.Evaluate(S("expression"))};
        if(name=="get_time"&&brain.Enabled("time"))return new{now=DateTimeOffset.Now.ToString("o"),timezone=TimeZoneInfo.Local.Id};
        if(name=="get_weather"&&brain.Enabled("weather")){
            var location=S("location");if(location.Length==0)location=brain.HomeCity;var places=await Get("https://geocoding-api.open-meteo.com/v1/search?count=1&name="+Uri.EscapeDataString(location));
            if(!places.TryGetProperty("results",out var results)||results.GetArrayLength()==0)return new{error="Location not found"};var place=results[0];var latitude=place.GetProperty("latitude").GetRawText();var longitude=place.GetProperty("longitude").GetRawText();
            var weather=await Get($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,relative_humidity_2m&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code&forecast_days=3&timezone=auto");return new{location,weather};
        }
        if(name is "web_search" or "encyclopedia_search"&&(brain.Enabled("web")||brain.Enabled("wikipedia"))){var query=Uri.EscapeDataString(S("query"));var wiki=await Get("https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=5&srsearch="+query);var pages=new List<JsonElement>();if(wiki.TryGetProperty("query",out var q)&&q.TryGetProperty("search",out var search))foreach(var page in search.EnumerateArray().Take(brain.Deep?3:1)){var title=page.GetProperty("title").GetString()!;pages.Add(await Get("https://en.wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&exintro=1&format=json&redirects=1&titles="+Uri.EscapeDataString(title)));}return new{source="Wikipedia",results=wiki,pages};}
        throw new InvalidOperationException("This tool is unavailable or disabled.");
    }
}
