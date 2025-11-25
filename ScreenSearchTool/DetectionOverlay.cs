using System;
using System.Drawing;
using System.Windows.Forms;

public class DetectionOverlay : Form
{
    private int _borderThickness = 4;
    private Color _borderColor = Color.Red; // Màu khung detect

    public DetectionOverlay(Rectangle area)
    {
        // Cấu hình cơ bản để ẩn tiêu đề, taskbar
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.StartPosition = FormStartPosition.Manual;

        // Đặt vị trí và kích thước khớp với vùng chọn (sel)
        // Mở rộng ra 1 chút để viền không che mất chữ bên trong (tuỳ chọn)
        this.Bounds = area;

        // Quan trọng: Màu nền và TransparencyKey giống nhau để làm trong suốt phần giữa
        this.BackColor = Color.Magenta;
        this.TransparencyKey = Color.Magenta;

        // Sự kiện vẽ khung
        this.Paint += DetectionOverlay_Paint;
    }

    // Ghi đè CreateParams để kích hoạt tính năng "xuyên thấu" (Click-through)
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // WS_EX_LAYERED (0x80000) + WS_EX_TRANSPARENT (0x20)
            // Giúp form trong suốt với chuột (click xuyên qua)
            cp.ExStyle |= 0x80000 | 0x20;
            return cp;
        }
    }

    private void DetectionOverlay_Paint(object sender, PaintEventArgs e)
    {
        // Vẽ viền chữ nhật
        using (Pen pen = new Pen(_borderColor, _borderThickness))
        {
            // Vẽ sát mép trong của Form
            e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
        }
    }
}