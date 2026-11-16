using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Migration;
namespace SecureClientPortal.Backend.Infrastructure.Modules.Migration;

public static class ClientSpreadsheetReader
{
    public const string Header = "name,entityType,industry,contactName,contactEmail,registrationNumber,accountantEmail";
    public static async Task<List<(int Row, MigrationClientInput Values)>> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > 2097152) throw new AppValidationException("Use a non-empty spreadsheet up to 2 MB.");
        await using var source = file.OpenReadStream();
        using var bounded = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0) {
            if (bounded.Length + read > 2097152) throw new AppValidationException("Spreadsheet exceeds 2 MB.");
            bounded.Write(buffer, 0, read);
        }
        bounded.Position = 0;
        List<List<string>> rows;
        try {
            if (Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) {
                using var reader = new StreamReader(bounded, new UTF8Encoding(false, true), true);
                rows = ReadCsv(await reader.ReadToEndAsync(ct));
            } else if (Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) rows = ReadXlsx(bounded);
            else throw new AppValidationException("Choose an Excel .xlsx or UTF-8 .csv file. Export older .xls files first.");
        } catch (Exception e) when (e is InvalidDataException or XmlException or DecoderFallbackException or ArgumentException) {
            throw new AppValidationException("The spreadsheet is damaged or unsupported. Export a fresh file using the template headers.");
        }
        if (rows.Count < 2 || rows.Count > 501) throw new AppValidationException("Include a header and 1 to 500 client rows.");
        var expected = Header.Split(',');
        var headers = rows[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Length != expected.Length || expected.Any(h => headers.Count(v => v.Equals(h, StringComparison.OrdinalIgnoreCase)) != 1))
            throw new AppValidationException("Headers must be: " + Header + ". Columns may be reordered.");
        var indices = expected.Select(h => Array.FindIndex(headers, v => v.Equals(h, StringComparison.OrdinalIgnoreCase))).ToArray();
        return rows.Skip(1).Select((r, i) => {
            if (r.Count != headers.Length) throw new AppValidationException($"Spreadsheet row {i + 2} must contain exactly seven columns.");
            var v = indices.Select(j => r[j].Trim()).ToArray();
            if (v.Any(s => s.Length > 500)) throw new AppValidationException($"Spreadsheet row {i + 2} contains an excessively long cell.");
            return (i + 2, new MigrationClientInput(v[0],v[1],v[2],v[3],v[4],v[5],v[6]));
        }).ToList();
    }
    public static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, closed = false;
        for (int i=0; i<text.Length; i++) {
            var c=text[i];
            if (quoted) {
                if (c=='"') { if (i+1<text.Length && text[i+1]=='"') {cell.Append('"');i++;} else {quoted=false;closed=true;} }
                else cell.Append(c);
            } else if (c=='"' && cell.Length==0 && !closed) quoted=true;
            else if (c==',' || c=='\r' || c=='\n') {
                row.Add(cell.ToString());cell.Clear();closed=false;
                if (c!=',') { if(c=='\r' && i+1<text.Length && text[i+1]=='\n')i++; rows.Add(row);row=[]; }
            } else { if(closed || c=='"') throw new AppValidationException("CSV contains an invalid quoted field."); cell.Append(c); }
            if (rows.Count>501 || row.Count>7 || cell.Length>500) throw new AppValidationException("Spreadsheet exceeds the row, column or cell limits.");
        }
        if (quoted) throw new AppValidationException("CSV contains an unfinished quoted field.");
        if(cell.Length>0 || row.Count>0 || closed) {row.Add(cell.ToString());rows.Add(row);}
        return rows;
    }
    private static List<List<string>> ReadXlsx(Stream source)
    {
        using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);
        if(zip.Entries.Count>200 || zip.Entries.Sum(e=>e.Length)>8388608) throw new AppValidationException("Excel workbook is too large when expanded. Use a smaller workbook.");
        XDocument Load(string path) {
            var entry=zip.GetEntry(path) ?? throw new InvalidDataException();
            using var s=entry.Open();
            using var reader=XmlReader.Create(s,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8388608});
            return XDocument.Load(reader);
        }
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheets=Load("xl/workbook.xml").Descendants(ns+"sheet").ToArray();
        if(sheets.Length!=1)throw new AppValidationException("Use a workbook containing exactly one worksheet for client imports.");
        var id=(string?)sheets[0].Attribute(rel+"id");
        var link=Load("xl/_rels/workbook.xml.rels").Root?.Elements().SingleOrDefault(e=>(string?)e.Attribute("Id")==id);
        var target=(string?)link?.Attribute("Target") ?? "";
        if((string?)link?.Attribute("TargetMode")=="External" || target.Contains("..") || target.Contains('\\'))throw new InvalidDataException();
        var path=target.StartsWith('/')?target.TrimStart('/'):"xl/"+target;
        if(!path.StartsWith("xl/worksheets/",StringComparison.Ordinal))throw new InvalidDataException();
        var strings=zip.GetEntry("xl/sharedStrings.xml") is null?[]:Load("xl/sharedStrings.xml").Descendants(ns+"si").Select(e=>string.Concat(e.Descendants(ns+"t").Select(t=>t.Value))).ToArray();
        var rows=new List<List<string>>();
        foreach(var row in Load(path).Descendants(ns+"sheetData").Elements(ns+"row")) {
            if(rows.Count>=501)throw new AppValidationException("Import at most 500 client rows at a time.");
            var values=Enumerable.Repeat("",7).ToList();
            foreach(var cell in row.Elements(ns+"c")) {
                if(cell.Element(ns+"f") is not null)throw new AppValidationException("Replace spreadsheet formulas with their values before importing.");
                var reference=(string?)cell.Attribute("r") ?? "";
                var column=new string(reference.TakeWhile(char.IsLetter).ToArray());
                if(column.Length!=1 || column[0]<'A' || column[0]>'G')throw new AppValidationException("Use only the seven template columns A to G.");
                var value=cell.Element(ns+"v")?.Value ?? string.Concat(cell.Descendants(ns+"t").Select(x=>x.Value));
                if((string?)cell.Attribute("t")=="s") {
                    if(!int.TryParse(value,out int index)||index<0||index>=strings.Length)throw new InvalidDataException(); value=strings[index];
                }
                values[column[0]-'A']=value;
            }
            // Preserve physical spreadsheet row numbers, including blank rows.
            var physical=(int?)row.Attribute("r") ?? rows.Count+1;
            if(physical != rows.Count+1)throw new AppValidationException("Remove blank rows between client records before importing.");
            rows.Add(values);
        }
        return rows;
    }
}
