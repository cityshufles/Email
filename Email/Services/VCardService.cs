using Org.BouncyCastle.Utilities;
using System.Text;

namespace Email.Services
{
    public class VCardService
    {
    //    private Task GenerateVCard()
    //    {
    //        var builder = WebApplication.CreateBuilder(args);
    //        var app = builder.Build();

    //        // Custom endpoint to serve vCards
    //        app.MapGet("/download-vcards", async context =>
    //{
    //    var vcards = new StringBuilder();

    //    // Example: generate multiple vCards
    //    vcards.AppendLine("BEGIN:VCARD");
    //    vcards.AppendLine("VERSION:3.0");
    //    vcards.AppendLine("FN:Test Contact");
    //    vcards.AppendLine("N:Contact;Test;;;");
    //    vcards.AppendLine("TEL;TYPE=CELL:+1234567890");
    //    vcards.AppendLine("END:VCARD");

    //    var bytes = Encoding.UTF8.GetBytes(vcards.ToString());

    //    context.Response.Headers.ContentType = "text/vcard";   // ✅ force correct MIME type
    //    context.Response.Headers.ContentLength = bytes.Length;
    //    context.Response.Headers.ContentDisposition =
    //        $"attachment; filename=\"contacts.vcf\"";

    //    await context.Response.Body.WriteAsync(bytes);
    //});

    //        app.Run();
    //        var builder = WebApplication.CreateBuilder(args);
    //        var app = builder.Build();

    //        // Custom endpoint to serve vCards
    //        app.MapGet("/download-vcards", async context =>
    //        {
    //            var vcards = new StringBuilder();

    //            // Example: generate multiple vCards
    //            vcards.AppendLine("BEGIN:VCARD");
    //            vcards.AppendLine("VERSION:3.0");
    //            vcards.AppendLine("FN:Test Contact");
    //            vcards.AppendLine("N:Contact;Test;;;");
    //            vcards.AppendLine("TEL;TYPE=CELL:+1234567890");
    //            vcards.AppendLine("END:VCARD");

    //            var bytes = Encoding.UTF8.GetBytes(vcards.ToString());

    //            context.Response.Headers.ContentType = "text/vcard";   // ✅ force correct MIME type
    //            context.Response.Headers.ContentLength = bytes.Length;
    //            context.Response.Headers.ContentDisposition =
    //                $"attachment; filename=\"contacts.vcf\"";

    //            await context.Response.Body.WriteAsync(bytes);
    //        });

    //        app.Run();


    //    }
    }
}
