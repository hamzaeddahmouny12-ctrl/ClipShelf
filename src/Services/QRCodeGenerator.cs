using System.Drawing;
using System.Windows.Media.Imaging;
using QRCoder;

namespace ClipShelf.Services
{
    public class QRCodeGenerator
    {
        public static BitmapImage GenerateQRCode(string data, int size = 300)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
                using (QRCode qrCode = new QRCode(qrCodeData))
                {
                    Bitmap qrCodeImage = qrCode.GetGraphic(20);

                    // Resize to desired size
                    Bitmap resized = new Bitmap(qrCodeImage, new System.Drawing.Size(size, size));

                    // Convert to BitmapImage
                    using (System.IO.MemoryStream memory = new System.IO.MemoryStream())
                    {
                        resized.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                        memory.Position = 0;
                        BitmapImage bitmapimage = new BitmapImage();
                        bitmapimage.BeginInit();
                        bitmapimage.StreamSource = memory;
                        bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapimage.EndInit();
                        return bitmapimage;
                    }
                }
            }
        }
    }
}
