namespace FCP.Core.Migration.Pcl;

/// <summary>
/// PCL 的内存滑块保存的是刻度值而不是 MB，这里换算成 MB。公式见 docs/02 §4。
/// </summary>
public static class PclRamScale
{
    public static double ToGigabytes(int scaleValue) => scaleValue switch
    {
        <= 12 => scaleValue * 0.1 + 0.3,
        <= 25 => (scaleValue - 12) * 0.5 + 1.5,
        <= 33 => (scaleValue - 25) * 1.0 + 8,
        _ => (scaleValue - 33) * 2.0 + 16,
    };

    public static int ToMegabytes(int scaleValue) => (int)Math.Floor(ToGigabytes(scaleValue) * 1024);
}
