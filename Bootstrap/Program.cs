using System.Net;
using System.Text;
using System.Text.Json;

var peers = new List<string>();

var listener = new HttpListener();
listener.Prefixes.Add("http://localhost:7000/");
listener.Start();

Console.WriteLine("SharkNet bootstrap, so welcome!");
Console.WriteLine("Bootstrap running on :7000");

while (true)
{
    var ctx = await listener.GetContextAsync();
    var path = ctx.Request.Url!.AbsolutePath;

    if (path == "/register" && ctx.Request.HttpMethod == "POST")
    {
        using var reader = new StreamReader(ctx.Request.InputStream);
        var url = await reader.ReadToEndAsync();

        if (!peers.Contains(url))
            peers.Add(url);

        var responseBytes = Encoding.UTF8.GetBytes("OK");
        await ctx.Response.OutputStream.WriteAsync(responseBytes);
        ctx.Response.Close();
    }

    else if (path == "/peers" && ctx.Request.HttpMethod == "GET")
    {
        var json = JsonSerializer.Serialize(peers);

        var buffer = Encoding.UTF8.GetBytes(json);

        ctx.Response.ContentType = "application/json";
        await ctx.Response.OutputStream.WriteAsync(buffer);

        ctx.Response.Close();
    }
}