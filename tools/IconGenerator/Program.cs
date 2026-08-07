using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var destination = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass the destination .ico path.");
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
var frames = sizes.Select(RenderPng).ToList();

using var output = new BinaryWriter(File.Create(destination));
output.Write((ushort)0);
output.Write((ushort)1);
output.Write((ushort)frames.Count);
var offset = 6 + frames.Count * 16;
for (var i = 0; i < frames.Count; i++)
{
    output.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    output.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    output.Write((byte)0);
    output.Write((byte)0);
    output.Write((ushort)1);
    output.Write((ushort)32);
    output.Write(frames[i].Length);
    output.Write(offset);
    offset += frames[i].Length;
}
foreach (var frame in frames) output.Write(frame);
Console.WriteLine(destination);
if (args.Length > 1)
{
    var preview = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
    File.WriteAllBytes(preview, frames[^1]);
    Console.WriteLine(preview);
}

static byte[] RenderPng(int size)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.CompositingQuality = CompositingQuality.HighQuality;
    graphics.Clear(Color.Transparent);
    var inset = Math.Max(1.0f, size * 0.035f);
    var rect = new RectangleF(inset, inset, size - inset * 2, size - inset * 2);
    var radius = size * 0.23f;
    using var path = RoundedRectangle(rect, radius);
    using var background = new LinearGradientBrush(rect, Color.FromArgb(102, 171, 247), Color.FromArgb(55, 113, 196), 55f);
    graphics.FillPath(background, path);
    using var sheen = new LinearGradientBrush(rect, Color.FromArgb(65, 255, 255, 255), Color.Transparent, 90f);
    graphics.FillPath(sheen, path);

    var lineWidth = Math.Max(1.45f, size * 0.085f);
    using var white = new Pen(Color.White, lineWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    var cx = size * 0.5f;
    graphics.DrawLine(white, cx, size * 0.22f, cx, size * 0.64f);
    graphics.DrawLine(white, size * 0.32f, size * 0.49f, cx, size * 0.67f);
    graphics.DrawLine(white, size * 0.68f, size * 0.49f, cx, size * 0.67f);
    graphics.DrawLine(white, size * 0.30f, size * 0.78f, size * 0.70f, size * 0.78f);

    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
{
    var diameter = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
    path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
    path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
    path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
    path.CloseFigure();
    return path;
}
