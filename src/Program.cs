using System.Globalization;
using System.Runtime.InteropServices;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace Tyler;

internal enum PaperSize
{
    A4,
    Letter,
}

internal enum PageOrientation
{
    Portrait,
    Landscape,
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message)
    {
    }
}

internal sealed class StructuralException : Exception
{
    public StructuralException(string message) : base(message)
    {
    }
}

/// <summary>
/// The reusable processing engine. Parses CLI arguments, slices the source
/// image, generates the PDF and returns a process exit code without touching
/// any UI. Both the CLI entry point (<see cref="Program.Main"/>) and the GUI
/// (TylerGui) call <see cref="TylerEngine.Run"/>; the result is identical on
/// every path so the CLI behaviour is preserved exactly.
/// </summary>
internal static class TylerEngine
{
    internal const int ExitSuccess = 0;
    internal const int ExitUnexpected = 1;
    internal const int ExitUsageError = 2;
    internal const int ExitStructuralError = 3;
    internal const int ExitIoError = 4;

    private const float PointsPerMillimetre = 2.83465f; // 72 pt/inch / 25.4 mm/inch
    internal const int MinColumns = 1;
    internal const int MaxColumns = 20;
    private const int MaxImageDimension = 100_000;

    // NOTE: the QuestPDF Community license is set in Program.Main as the very
    // first executable statement of the process. It is intentionally NOT set
    // here so the license line never runs twice.

    public static int Run(string[] args) => Run(args, out _);

    /// <summary>
    /// Runs the pipeline and additionally returns a human-readable message
    /// describing the outcome (the output path on success, or the error text).
    /// The CLI entry point uses the plain <see cref="Run(string[])"/> overload
    /// so its console behaviour is byte-for-byte unchanged; the GUI uses this
    /// overload to populate its result dialog.
    /// </summary>
    internal static int Run(string[] args, out string? message)
    {
        message = null;
        try
        {
            if (WantsHelp(args))
            {
                WriteHelp(Console.Out);
                return ExitSuccess;
            }

            if (!CliOptions.TryParse(args, out CliOptions options, out string error))
            {
                Console.Error.WriteLine($"ERROR: {error}");
                Console.Error.WriteLine("Run with '--help' for usage information.");
                message = error;
                return ExitUsageError;
            }

            return RunPipeline(options, out message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            message = ex.Message;
            return ExitUnexpected;
        }
    }

    internal static bool WantsHelp(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Tyler");
        writer.WriteLine("Splits a long chat screenshot into a grid of tiles and lays them out on");
        writer.WriteLine("multi-page PDF pages, filling columns top-to-bottom then left-to-right.");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  Tyler --input <image> --output <pdf> [options]");
        writer.WriteLine();
        writer.WriteLine("Arguments:");
        writer.WriteLine("  --input <path>    (required) Path to the input image (PNG, JPEG or BMP).");
        writer.WriteLine("  --output <path>   (required) Path of the generated multi-page PDF file.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine($"  --paper-size <name>  A4 | Letter            (default: {CliOptions.DefaultPaperSizeName})");
        writer.WriteLine($"  --orientation <name> Portrait | Landscape   (default: {CliOptions.DefaultOrientationName})");
        writer.WriteLine($"  --columns <int>      Tiles per page          (default: {CliOptions.DefaultColumns}, range {MinColumns}..{MaxColumns})");
        writer.WriteLine($"  --margin <float>     Page margin in mm       (default: {CliOptions.DefaultMarginMillimetres:0.#}, must be >= 0)");
        writer.WriteLine($"  --overlap <float>    Vertical tile overlap % (default: {CliOptions.DefaultOverlapPercent:0.#}, range 0..100)");
        writer.WriteLine();
        writer.WriteLine("Both '--name value' and '--name=value' forms are accepted. Flag names are");
        writer.WriteLine("case-insensitive. Unknown arguments are rejected with a usage error.");
        writer.WriteLine();
        writer.WriteLine("Exit codes:");
        writer.WriteLine("  0  success");
        writer.WriteLine("  1  unexpected error");
        writer.WriteLine("  2  usage / argument error");
        writer.WriteLine("  3  structural / layout error (invalid image or geometry)");
        writer.WriteLine("  4  I/O error (reading the input or writing the PDF)");
        writer.WriteLine();
        writer.WriteLine("Examples:");
        writer.WriteLine("  Tyler --input chat.png --output chat.pdf");
        writer.WriteLine("  Tyler --input=chat.png --output=chat.pdf");
        writer.WriteLine("      --paper-size=Letter --orientation=Landscape --columns=3 --margin=8 --overlap=3");
    }

    // =========================================================================
    // Pipeline: load -> validate -> slice -> compose PDF
    // =========================================================================

    private static int RunPipeline(CliOptions options, out string? message)
    {
        message = null;
        try
        {
            // STEP B: load and validate the source image.
            using ImageSharpImage source = LoadSourceImage(options.InputPath);
            Layout layout = ComputeLayout(source, options);

            // STEP D: slice the image vertically with the requested overlap.
            List<byte[]> slices = SliceImage(source, layout);

            // STEP E: compose the multi-page PDF.
            GeneratePdf(layout, slices, options.OutputPath);

            Console.WriteLine(
                $"OK: {slices.Count} slice(s) laid out as {layout.Columns} column(s) on " +
                $"{layout.PageCount} page(s) -> {options.OutputPath}");

            message = options.OutputPath;
            return ExitSuccess;
        }
        catch (CliUsageException)
        {
            throw; // handled in Run
        }
        catch (StructuralException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            message = ex.Message;
            return ExitStructuralError;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ERROR: I/O failure: {ex.Message}");
            message = ex.Message;
            return ExitIoError;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"ERROR: access denied: {ex.Message}");
            message = ex.Message;
            return ExitIoError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: unexpected failure: {ex.Message}");
            message = ex.Message;
            return ExitUnexpected;
        }
    }

    private static ImageSharpImage LoadSourceImage(string path)
    {
        if (!File.Exists(path))
        {
            throw new StructuralException($"input file not found: '{path}'");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            ImageSharpImage image = (ImageSharpImage)ImageSharpImage.Load(stream);

            if (image.Width <= 0 || image.Height <= 0)
            {
                image.Dispose();
                throw new StructuralException($"input image has invalid dimensions: {image.Width}x{image.Height}");
            }

            if (image.Width > MaxImageDimension || image.Height > MaxImageDimension)
            {
                image.Dispose();
                throw new StructuralException(
                    $"input image is too large ({image.Width}x{image.Height}); maximum dimension is {MaxImageDimension}px");
            }

            // STEP B: the source must be a long vertical strip.
            if (image.Height <= image.Width * 1.5)
            {
                image.Dispose();
                throw new StructuralException(
                    $"input image is not a long vertical strip: {image.Width}x{image.Height} " +
                    "(height must exceed 1.5x width)");
            }

            return image;
        }
        catch (Exception ex) when (ex is not StructuralException)
        {
            throw new StructuralException($"failed to load image '{path}': {ex.Message}");
        }
    }

    // =========================================================================
    // Layout geometry
    // =========================================================================

    private sealed record Layout(
        int ImageWidth,
        int ImageHeight,
        float PageWidth,
        float PageHeight,
        float MarginPoints,
        float PrintableWidth,
        float PrintableHeight,
        float ColumnWidthPoints,
        float ColumnGapPoints,
        float Scale,
        float MaxSliceHeightPixels,
        float OverlapPixels,
        int Columns,
        int PageCount,
        string PaperSizeLabel,
        string OrientationLabel);

    private static Layout ComputeLayout(ImageSharpImage source, CliOptions options)
    {
        float pageWidth;
        float pageHeight;

        switch (options.PaperSize)
        {
            case PaperSize.A4:
                pageWidth = 595f;
                pageHeight = 842f;
                break;
            case PaperSize.Letter:
                pageWidth = 612f;
                pageHeight = 792f;
                break;
            default:
                throw new StructuralException("unsupported paper size");
        }

        if (options.Orientation == PageOrientation.Landscape)
        {
            (pageWidth, pageHeight) = (pageHeight, pageWidth);
        }

        float marginPoints = options.MarginMillimetres * PointsPerMillimetre;
        float printableWidth = pageWidth - (2f * marginPoints);
        float printableHeight = pageHeight - (2f * marginPoints);

        if (printableWidth <= 0f || printableHeight <= 0f)
        {
            throw new StructuralException(
                $"margin {options.MarginMillimetres.ToString(CultureInfo.InvariantCulture)}mm leaves no " +
                "printable area on the page");
        }

        int columns = options.Columns;
        float columnGapPoints = marginPoints;
        float totalGapPoints = columnGapPoints * (columns - 1);

        if (totalGapPoints >= printableWidth)
        {
            throw new StructuralException(
                $"{columns} column(s) with {options.MarginMillimetres.ToString(CultureInfo.InvariantCulture)}mm " +
                "gaps exceed the printable width");
        }

        float columnWidthPoints = (printableWidth - totalGapPoints) / columns;

        if (columnWidthPoints <= 0f || !float.IsFinite(columnWidthPoints))
        {
            throw new StructuralException("computed column width is invalid");
        }

        float scale = columnWidthPoints / source.Width;

        if (scale <= 0f || !float.IsFinite(scale))
        {
            throw new StructuralException("computed image scale is invalid");
        }

        float maxSliceHeightPixels = printableHeight / scale;
        float overlapPixels = maxSliceHeightPixels * (options.OverlapPercent / 100f);

        // Number of slices, based on the effective step between slice starts.
        int sliceCount = ComputeSliceCount(source.Height, maxSliceHeightPixels, overlapPixels);

        if (sliceCount <= 0)
        {
            throw new StructuralException("computed zero slices; image too small for the page grid");
        }

        int pageCount = Math.Max(1, (sliceCount + columns - 1) / columns);

        return new Layout(
            source.Width,
            source.Height,
            pageWidth,
            pageHeight,
            marginPoints,
            printableWidth,
            printableHeight,
            columnWidthPoints,
            columnGapPoints,
            scale,
            maxSliceHeightPixels,
            overlapPixels,
            columns,
            pageCount,
            options.PaperSize.ToString(),
            options.Orientation.ToString());
    }

    /// <summary>
    /// Computes how many vertical slices cover the whole image when each new
    /// slice starts at (previous end - overlap).
    /// </summary>
    private static int ComputeSliceCount(int imageHeight, float sliceHeightPixels, float overlapPixels)
    {
        if (sliceHeightPixels <= 0f)
        {
            throw new StructuralException("slice height must be positive");
        }

        if (overlapPixels < 0f || overlapPixels >= sliceHeightPixels)
        {
            // Degenerate: overlap >= slice height never advances. Treat as no overlap.
            overlapPixels = 0f;
        }

        float step = sliceHeightPixels - overlapPixels;

        if (step <= 0f)
        {
            // Still degenerate (floating point); fall back to no overlap.
            step = sliceHeightPixels;
        }

        // ceil(total / step) gives the count of starts needed to cover the image.
        int count = (int)MathF.Ceiling(imageHeight / step);
        return Math.Max(1, count);
    }

    // =========================================================================
    // Image slicing (ImageSharp)
    // =========================================================================

    private static List<byte[]> SliceImage(ImageSharpImage source, Layout layout)
    {
        var slices = new List<byte[]>(layout.PageCount * layout.Columns);

        int sliceHeight = (int)MathF.Floor(layout.MaxSliceHeightPixels);
        int overlap = (int)MathF.Floor(layout.OverlapPixels);

        if (sliceHeight < 1)
        {
            throw new StructuralException("computed slice height is smaller than 1 pixel");
        }

        int y = 0;
        while (y < source.Height)
        {
            int height = Math.Min(sliceHeight, source.Height - y);

            if (height < 1)
            {
                break;
            }

            slices.Add(CropToPngBytes(source, 0, y, source.Width, height));

            // The slice reached the bottom edge: the image is fully covered and
            // no further slices are needed. Without this check, a trailing slice
            // whose remaining height is <= overlap would make the next start
            // equal to the current y, forcing a 1px "advance" and producing a
            // long tail of single-pixel slices.
            if (y + height >= source.Height)
            {
                break;
            }

            // Next slice starts at (previous end - overlap), but must advance.
            int nextY = y + height - overlap;
            if (nextY <= y)
            {
                nextY = y + 1; // guarantee forward progress
            }

            y = nextY;
        }

        if (slices.Count == 0)
        {
            throw new StructuralException("no slices were produced from the source image");
        }

        return slices;
    }

    private static byte[] CropToPngBytes(ImageSharpImage source, int x, int y, int width, int height)
    {
        using ImageSharpImage crop = source.Clone(ctx => ctx.Crop(new Rectangle(x, y, width, height)));
        using var stream = new MemoryStream();
        crop.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    // =========================================================================
    // PDF generation (QuestPDF)
    // =========================================================================

    private static void GeneratePdf(Layout layout, List<byte[]> slices, string outputPath)
    {
        try
        {
            string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // The QuestPDF fluent API fills a Row left-to-right; each page holds
            // exactly N slices (one per column slot), and each slice already has
            // the aspect ratio ColumnWidthPoints x PrintableHeight by construction
            // (see ComputeLayout). Pages are therefore produced in strict
            // column-major reading order: column 1 top-to-bottom, then column 2,
            // etc., and the page count equals ceil(slices / columns).
            //
            // No text is ever rendered and no system font (Arial/Helvetica) is
            // referenced, so the embedded "Lato" font supplied by QuestPDF is
            // never consulted and no missing-font exception can occur.
            Document.Create(container =>
            {
                int sliceIndex = 0;

                while (sliceIndex < slices.Count)
                {
                    int pageSliceCount = Math.Min(layout.Columns, slices.Count - sliceIndex);

                    container.Page(page =>
                    {
                        // Exact paper size (points) and margins from the layout.
                        page.Size(layout.PageWidth, layout.PageHeight, Unit.Point);
                        page.Margin(layout.MarginPoints, Unit.Point);

                        page.Content().Row(row =>
                        {
                            for (int c = 0; c < pageSliceCount; c++)
                            {
                                // Gap between grid columns (== margin width).
                                if (c > 0)
                                {
                                    row.ConstantItem(layout.ColumnGapPoints, Unit.Point);
                                }

                                row.ConstantItem(layout.ColumnWidthPoints, Unit.Point)
                                    .Height(layout.PrintableHeight, Unit.Point)
                                    .Image(slices[sliceIndex + c])
                                    .FitWidth();
                            }
                        });
                    });

                    sliceIndex += pageSliceCount;
                }
            }).GeneratePdf(outputPath);
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StructuralException)
        {
            throw new StructuralException($"failed to generate PDF: {ex.Message}");
        }
    }
}

/// <summary>
/// Process entry point. With no arguments the app shows the native GUI when an
/// interactive display is available (headless machines fall back to printing
/// the help screen); with any arguments it runs the CLI exactly as before.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        // STEP A (license): must be the very first executable statement.
        // This application is distributed free on GitHub, so the Community
        // license applies and no paid features are used.
        QuestPDF.Settings.License = LicenseType.Community;

        // STEP A (fonts): QuestPDF embeds the "Lato" font in its assembly, and
        // this program never references system fonts such as "Arial"/"Helvetica"
        // nor registers any external .ttf file. The PDF engine therefore cannot
        // throw a missing-font exception, even on a minimal Linux/ARM64 Docker
        // image with no fonts installed. No FontManager registration is needed.

        try
        {
            if (args.Length > 0)
            {
                // Any arguments => the CLI path, exactly as before. The hidden
                // documentation flag --gui-screenshot is intercepted here and is
                // intentionally NOT part of TylerEngine's parsing or --help text.
                if (TylerGui.TryGetScreenshotPath(args, out string? screenshotPath))
                {
                    return TylerGui.RunScreenshot(screenshotPath!);
                }

                return TylerEngine.Run(args);
            }

            // No arguments: show the native GUI when an interactive desktop is
            // available; otherwise mirror the CLI help screen and exit 0 so the
            // tool stays scriptable in headless/session-0/CI environments.
            if (TylerGui.IsGuiAvailable())
            {
                return TylerGui.RunGui();
            }

            TylerEngine.WriteHelp(Console.Out);
            return TylerEngine.ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return TylerEngine.ExitUnexpected;
        }
    }
}

/// <summary>
/// Immutable CLI options with defaults, parsed without any reflection or
/// dependency injection. All numeric parsing uses the invariant culture so the
/// behaviour is identical on every locale and under Native AOT.
/// </summary>
internal readonly record struct CliOptions
{
    public const string DefaultPaperSizeName = "A4";
    public const string DefaultOrientationName = "Portrait";
    public const int DefaultColumns = 2;
    public const float DefaultMarginMillimetres = 10.0f;
    public const float DefaultOverlapPercent = 5.0f;

    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required PaperSize PaperSize { get; init; }
    public required PageOrientation Orientation { get; init; }
    public required int Columns { get; init; }
    public required float MarginMillimetres { get; init; }
    public required float OverlapPercent { get; init; }

    public static bool TryParse(string[] args, out CliOptions options, out string error)
    {
        options = default;
        error = string.Empty;

        string? input = null;
        string? output = null;
        string paperSizeName = DefaultPaperSizeName;
        string orientationName = DefaultOrientationName;
        int columns = DefaultColumns;
        float marginMillimetres = DefaultMarginMillimetres;
        float overlapPercent = DefaultOverlapPercent;

        bool anyUnknown = false;
        string firstUnknown = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];

            // Split "--name=value" into its name and value parts.
            string name;
            string? inlineValue = null;
            int equalsIndex = token.IndexOf('=');
            if (equalsIndex >= 0)
            {
                name = token[..equalsIndex];
                inlineValue = token[(equalsIndex + 1)..];
            }
            else
            {
                name = token;
            }

            // Resolve the value: either the inline part or the next argument.
            string? value = inlineValue;

            switch (name.ToLowerInvariant())
            {
                case "--input":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--input expects a value"); }
                    }
                    input = value;
                    break;

                case "--output":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--output expects a value"); }
                    }
                    output = value;
                    break;

                case "--paper-size":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--paper-size expects a value"); }
                    }
                    paperSizeName = value!;
                    break;

                case "--orientation":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--orientation expects a value"); }
                    }
                    orientationName = value!;
                    break;

                case "--columns":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--columns expects a value"); }
                    }
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columns))
                    {
                        return Fail(ref options, ref error, $"--columns must be an integer, got '{value}'");
                    }
                    break;

                case "--margin":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--margin expects a value"); }
                    }
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out marginMillimetres))
                    {
                        return Fail(ref options, ref error, $"--margin must be a number, got '{value}'");
                    }
                    break;

                case "--overlap":
                    if (inlineValue is null)
                    {
                        if (!TryTakeNext(args, ref i, out value)) { return Fail(ref options, ref error, "--overlap expects a value"); }
                    }
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out overlapPercent))
                    {
                        return Fail(ref options, ref error, $"--overlap must be a number, got '{value}'");
                    }
                    break;

                default:
                    anyUnknown = true;
                    if (firstUnknown.Length == 0) { firstUnknown = name; }
                    break;
            }
        }

        // Reject unknown flags.
        if (anyUnknown)
        {
            return Fail(ref options, ref error, $"unknown argument '{firstUnknown}'");
        }

        // Validate required arguments.
        if (string.IsNullOrWhiteSpace(input))
        {
            return Fail(ref options, ref error, "missing required argument '--input'");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return Fail(ref options, ref error, "missing required argument '--output'");
        }

        // Validate ranges.
        if (columns < TylerEngine.MinColumns || columns > TylerEngine.MaxColumns)
        {
            return Fail(ref options, ref error, $"--columns must be between {TylerEngine.MinColumns} and {TylerEngine.MaxColumns}, got {columns}");
        }

        if (!float.IsFinite(marginMillimetres) || marginMillimetres < 0f)
        {
            return Fail(ref options, ref error, $"--margin must be >= 0, got {marginMillimetres.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!float.IsFinite(overlapPercent) || overlapPercent < 0f || overlapPercent > 100f)
        {
            return Fail(ref options, ref error, $"--overlap must be between 0 and 100, got {overlapPercent.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!TryParsePaperSize(paperSizeName, out PaperSize paperSize))
        {
            return Fail(ref options, ref error, $"--paper-size must be 'A4' or 'Letter', got '{paperSizeName}'");
        }

        if (!TryParseOrientation(orientationName, out PageOrientation orientation))
        {
            return Fail(ref options, ref error, $"--orientation must be 'Portrait' or 'Landscape', got '{orientationName}'");
        }

        options = new CliOptions
        {
            InputPath = input!,
            OutputPath = output!,
            PaperSize = paperSize,
            Orientation = orientation,
            Columns = columns,
            MarginMillimetres = marginMillimetres,
            OverlapPercent = overlapPercent,
        };

        return true;
    }

    private static bool TryTakeNext(string[] args, ref int i, out string? value)
    {
        if (i + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        i++;
        value = args[i];
        return true;
    }

    private static bool Fail(ref CliOptions options, ref string error, string message)
    {
        options = default;
        error = message;
        return false;
    }

    private static bool TryParsePaperSize(string name, out PaperSize paperSize)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "a4":
                paperSize = PaperSize.A4;
                return true;
            case "letter":
                paperSize = PaperSize.Letter;
                return true;
            default:
                paperSize = default;
                return false;
        }
    }

    private static bool TryParseOrientation(string name, out PageOrientation orientation)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "portrait":
                orientation = PageOrientation.Portrait;
                return true;
            case "landscape":
                orientation = PageOrientation.Landscape;
                return true;
            default:
                orientation = default;
                return false;
        }
    }
}
