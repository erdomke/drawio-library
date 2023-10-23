using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DrawIoLibraryCleanup
{
  class Program
  {
    private const string sourcePath = @"";
    private const string targetPath = @"";

    static async Task Main(string[] args)
    {
      ProcessClarityFiles();
      await ProcessMaterialIcons();
    }

    private static void ListLibraries(string directory)
    {
      var json = JsonSerializer.Serialize(Directory.GetFiles(directory, "*.drawio")
        .Select(d => new Dictionary<string, object>()
        {
          { "file", d },
          { "libName", Path.GetFileNameWithoutExtension(d) }
        })
        .ToList());
      ;
    }

    private const string materialIconIndex = @"http://fonts.google.com/metadata/icons?incomplete=1&key=material_symbols";

    //https://raw.githubusercontent.com/google/material-design-icons/master/symbols/web/10k/materialsymbolsoutlined/10k_24px.svg
    private static async Task ProcessMaterialIcons()
    {
      var client = new HttpClient();
      var styles = new[] { "default", "fill1" };
      var json = await client.GetStringAsync(materialIconIndex);
      using (var doc = JsonDocument.Parse(json.Substring(5)))
      {
        var host = doc.RootElement.GetProperty("host").GetString();
        foreach (var group in doc.RootElement.GetProperty("icons")
          .EnumerateArray()
          .Where(i => !i.GetProperty("unsupported_families")
            .EnumerateArray()
            .Any(v => string.Equals(v.GetString(), "Material Symbols Outlined", StringComparison.OrdinalIgnoreCase))
          )
          .GroupBy(i => i.GetProperty("categories").EnumerateArray().First().GetString(), StringComparer.OrdinalIgnoreCase))
        {
          var title = "Material Symbol - " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Key.Replace('-', ' '));
          var target = Path.Combine(targetPath, title + ".drawio");
          var svgs = new List<object>();
          foreach (var icon in group)
          {
            var name = icon.GetProperty("name").GetString();
            Console.WriteLine($"Processing {name}");
            foreach (var style in styles)
            {
              var url = $"https://{host}/s/i/short-term/release/materialsymbolsoutlined/{name}/{style}/24px.svg";
              using (var stream = await client.GetStreamAsync(url))
                svgs.Add(SvgToDrawIo(stream, name));
            }
          }

          using (var libraryWriter = new StreamWriter(target))
          {
            libraryWriter.Write($"<mxlibrary title='{title}'>");
            libraryWriter.Write(JsonSerializer.Serialize(svgs));
            libraryWriter.Write("</mxlibrary>");
          }
        }
      }
    }

    private static void ProcessClarityFiles()
    {
      foreach (var directory in Directory.GetDirectories(sourcePath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
      {
        CreateLibrary(directory);
      }
    }

    private static void CreateLibrary(string directory)
    {
      var title = "Clarity - " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Path.GetFileName(directory).Replace('-', ' '));
      var target = Path.Combine(targetPath, title + ".drawio");
      
      var svgs = Directory.GetFiles(directory, "*.svg")
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .Select(svg =>
        {
          using (var stream = File.OpenRead(svg))
            return SvgToDrawIo(stream, Path.GetFileNameWithoutExtension(svg));
        })
        .ToList();

      using (var libraryWriter = new StreamWriter(target))
      {
        libraryWriter.Write($"<mxlibrary title='{title}'>");
        libraryWriter.Write(JsonSerializer.Serialize(svgs));
        libraryWriter.Write("</mxlibrary>");
      }
    }

    private static Dictionary<string, object> SvgToDrawIo(Stream stream, string name)
    {
      var svgNs = (XNamespace)"http://www.w3.org/2000/svg";
      var svg = XElement.Load(stream);
      var termsToSkip = new HashSet<string>() { "line", "outline", "solid", "alerted", "badged" };
      var titleParts = name.Split(new[] { '-', '_' });
      var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(" ", titleParts
        .Reverse()
        .SkipWhile(t => termsToSkip.Contains(t))
        .Reverse()));
      svg.Element(svgNs + "title")?.Remove();
      svg.Elements(svgNs + "rect").LastOrDefault()?.Remove();

      var group = svg.Element(svgNs + "g");
      while (group != null)
      {
        group.AddBeforeSelf(group.Elements().ToArray());
        group.Remove();
        group = svg.Element(svgNs + "g");
      }

      var styleList = new HashSet<string>();
      foreach (var element in svg.Elements())
      {
        var classes = new HashSet<string>(((string)element.Attribute("class") ?? "").Split(' ')
          .Where(c => !string.IsNullOrEmpty(c)));
        var style = "Main";
        if (classes.Contains("clr-i-badge"))
          style = "Badge";
        else if (classes.Contains("clr-i-alert"))
          style = "Alert";
        styleList.Add(style);
        element.SetAttributeValue("class", style);
      }
      var styleElem = new XElement(svgNs + "style"
        , new XAttribute("type", "text/css")
        , string.Join(" ", styleList.Select(s => $".{s} {{ fill: #000000; }}")));
      svg.AddFirst(styleElem);
      var xmlString = svg.ToString(SaveOptions.DisableFormatting);

      var viewBoxParts = ((string)svg.Attribute("viewBox") ?? "").Split(' ');
      var width = (int?)svg.Attribute("width") ?? int.Parse(viewBoxParts[2]);
      var height = (int?)svg.Attribute("height") ?? int.Parse(viewBoxParts[3]);

      var mxGraph = new XElement("mxGraphModel"
        , new XElement("root"
          , new XElement("mxCell", new XAttribute("id", "0"))
          , new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0"))
          , new XElement("mxCell", new XAttribute("id", "2"), new XAttribute("value", "")
            , new XAttribute("style", "shape=image;editableCssRules=.*;verticalLabelPosition=bottom;verticalAlign=top;imageAspect=0;aspect=fixed;image=data:image/svg+xml," + Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlString)) + ";fillColor=#000000;")
            , new XAttribute("vertex", "1")
            , new XAttribute("parent", "1")
            , new XElement("mxGeometry"
              , new XAttribute("width", width)
              , new XAttribute("height", height)
              , new XAttribute("as", "geometry")
            )
          )
        )
      ).ToString(SaveOptions.DisableFormatting);
      var zippedStream = new MemoryStream();
      using (var compress = new DeflateStream(zippedStream, CompressionMode.Compress, true))
      {
        var data = Encoding.UTF8.GetBytes(Uri.EscapeDataString(mxGraph));
        compress.Write(data, 0, data.Length);
      }

      return new Dictionary<string, object>()
      {
        { "xml", Convert.ToBase64String(zippedStream.ToArray()) },
        { "w", width },
        { "h", height },
        { "title", title },
        { "aspect", "fixed" }
      };
    }
  }
}
