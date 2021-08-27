using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DrawIoLibraryCleanup
{
  class Program
  {
    private const string sourcePath = @"";
    private const string targetPath = @"";

    static void Main(string[] args)
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
      using (var libraryWriter = new StreamWriter(target))
      {
        libraryWriter.Write($"<mxlibrary title='{title}'>[");
        var first = true;
        foreach (var svg in Directory.GetFiles(directory, "*.svg").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
          if (first)
            first = false;
          else
            libraryWriter.Write(",");
          libraryWriter.Write(SvgToDrawIo(svg));
        }
        libraryWriter.Write("]</mxlibrary>");
      }
    }

    private static string SvgToDrawIo(string path)
    {
      var svgNs = (XNamespace)"http://www.w3.org/2000/svg";
      var svg = XElement.Load(path);
      var termsToSkip = new HashSet<string>() { "line", "outline", "solid", "alerted", "badged" };
      var titleParts = Path.GetFileNameWithoutExtension(path).Split('-');
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

      var mxGraph = new XElement("mxGraphModel"
        , new XElement("root"
          , new XElement("mxCell", new XAttribute("id", "0"))
          , new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0"))
          , new XElement("mxCell", new XAttribute("id", "2"), new XAttribute("value", "")
            , new XAttribute("style", "shape=image;editableCssRules=.*;verticalLabelPosition=bottom;verticalAlign=top;imageAspect=0;aspect=fixed;image=data:image/svg+xml," + Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlString)) + ";fillColor=#000000;")
            , new XAttribute("vertex", "1")
            , new XAttribute("parent", "1")
            , new XElement("mxGeometry"
              , new XAttribute("width", (int)svg.Attribute("width"))
              , new XAttribute("height", (int)svg.Attribute("height"))
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

      var jsonDict = new Dictionary<string, object>()
      {
        { "xml", Convert.ToBase64String(zippedStream.ToArray()) },
        { "w", (int)svg.Attribute("width") },
        { "h", (int)svg.Attribute("height") },
        { "title", title },
        { "aspect", "fixed" }
      };
      return JsonConvert.SerializeObject(jsonDict);
    }
  }
}
