using System.IO.Compression;
using System.Text;
using System.Xml;
using UglyToad.PdfPig;
namespace AuraOne;
public sealed record DocumentText(string Name,string Format,string Text,bool Truncated,int Pages);
public static class DocumentReader
{
 public const int MaxBytes=25*1024*1024,MaxCharacters=60000;
 public static async Task<DocumentText> Read(Stream source,string name,string mime,CancellationToken ct=default)
 {
  using var bytes=new MemoryStream();var buffer=new byte[16384];int count;
  while((count=await source.ReadAsync(buffer,ct))>0){if(bytes.Length+count>MaxBytes)throw new InvalidOperationException("Choose a document up to 25 MB.");await bytes.WriteAsync(buffer.AsMemory(0,count),ct);}
  var data=bytes.ToArray();var extension=Path.GetExtension(name).ToLowerInvariant();var text=new StringBuilder();bool truncated=false;int pages=0;
  void Append(string value){if(text.Length>=MaxCharacters){truncated=true;return;}var left=MaxCharacters-text.Length;if(value.Length>left){text.Append(value.AsSpan(0,left));truncated=true;}else text.Append(value);}
  if(mime=="application/pdf"||extension==".pdf"||data.AsSpan().StartsWith("%PDF-"u8))
  {
   using var pdf=PdfDocument.Open(data);pages=pdf.NumberOfPages;
   for(int page=1;page<=Math.Min(pages,100)&&text.Length<MaxCharacters;page++){ct.ThrowIfCancellationRequested();Append(pdf.GetPage(page).Text);Append("\n");}
   truncated|=pages>100;
   if(string.IsNullOrWhiteSpace(text.ToString()))throw new InvalidOperationException("This PDF has no extractable text. Share page images for visual reading.");
   return new(name,"PDF",text.ToString(),truncated,pages);
  }
  if(extension is ".docx" or ".pptx" or ".xlsx" or ".odt" or ".ods" or ".odp"||mime.Contains("officedocument")||mime.Contains("opendocument"))
  {
   using var archive=new ZipArchive(new MemoryStream(data),ZipArchiveMode.Read);
   if(archive.Entries.Count>3000||archive.Entries.Sum(x=>x.Length)>128L*1024*1024)throw new InvalidOperationException("The Office archive exceeds its reading limit.");
   var entries=archive.Entries.Where(e=>e.FullName=="word/document.xml"||e.FullName=="content.xml"||e.FullName=="xl/sharedStrings.xml"||e.FullName.StartsWith("xl/worksheets/sheet")||e.FullName.StartsWith("ppt/slides/slide")&&e.FullName.EndsWith(".xml"))
    .OrderBy(e=>e.FullName.StartsWith("xl/sharedStrings")?0:1).ThenBy(e=>System.Text.RegularExpressions.Regex.Replace(e.FullName,@"\d+",n=>n.Value.PadLeft(10,'0')),StringComparer.Ordinal).ToArray();
   var shared=new List<string>();
   foreach(var entry in entries)
   {
    ct.ThrowIfCancellationRequested();if(entry.Length>16*1024*1024)throw new InvalidOperationException("An Office document section exceeds its size limit.");
    using var input=entry.Open();using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024});
    if(entry.FullName=="xl/sharedStrings.xml")
    {var value=new StringBuilder();while(reader.Read()){if(reader.NodeType==XmlNodeType.Element&&reader.LocalName=="si")value.Clear();else if(reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)value.Append(reader.Value);else if(reader.NodeType==XmlNodeType.EndElement&&reader.LocalName=="si"){shared.Add(value.ToString());if(shared.Count>100000)throw new InvalidOperationException("Spreadsheet has too many shared values.");}}continue;}
    bool sheet=entry.FullName.StartsWith("xl/worksheets/"),sharedCell=false;string? cellType=null;
    while(reader.Read())
    {
     ct.ThrowIfCancellationRequested();
     if(reader.NodeType==XmlNodeType.Element&&sheet&&reader.LocalName=="c")cellType=reader.GetAttribute("t");
     if(reader.NodeType==XmlNodeType.Element&&sheet&&reader.LocalName=="v")sharedCell=cellType=="s";
     if(reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
     {if(sheet&&sharedCell&&int.TryParse(reader.Value,out var index)&&index>=0&&index<shared.Count)Append(shared[index]);else Append(reader.Value);Append(" ");}
     if(reader.NodeType==XmlNodeType.EndElement)
     {if(reader.LocalName is "p" or "row")Append("\n");if(reader.LocalName=="v")sharedCell=false;}
     if(text.Length>=MaxCharacters){truncated=true;break;}
    }
    pages++;Append("\n");if(text.Length>=MaxCharacters)break;
   }
   if(entries.Length==0||text.Length==0)throw new InvalidOperationException("No supported text was found in this Office document.");
   return new(name,extension.TrimStart('.').ToUpperInvariant(),text.ToString(),truncated,pages);
  }
  if(mime.StartsWith("text/")||mime is "application/json" or "application/xml"||extension is ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".log")
  {Append(Encoding.UTF8.GetString(data));return new(name,"Text",text.ToString(),truncated,1);}
  throw new InvalidOperationException("Choose PDF, DOCX, PPTX, XLSX, OpenDocument, image or plain text. Legacy binary Office formats need conversion first.");
 }
}
