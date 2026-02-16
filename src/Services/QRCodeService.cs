using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace ClipShelf.Services;

/// <summary>Generates QR codes for Phone Sync URL (http://IP:PORT?pin=PIN).</summary>
public static class QRCodeService
{
    public static BitmapImage Generate(string data, int pixelSize = 300)
    {
        using var qrGen = new QRCoder.QRCodeGenerator();
        using QRCoder.QRCodeData qrData = qrGen.CreateQrCode(data, QRCoder.QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new QRCoder.QRCode(qrData);
        using var bitmap = qrCode.GetGraphic(20);

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = ms;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
