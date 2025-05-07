using System.Collections.Concurrent;
using System.Text.Json;

namespace CMap_scraper
{
    class Program
    {
        // Web Mercator constants
        private const double EarthRadius = 6378137;
        private const int MaxConcurrentRequests = 50; // Adjust based on your system's capacity

        static async Task Main(string[] args)
        {
            // Hardcoded bounding box coordinates (lat/lon)
            double minLat = 55.978562;
            double minLon = 9.431488;
            double maxLat = 56.215258;
            double maxLon = 10.162766;
            int minZoom = 10;

            Console.Write("Max Zoom (Dont do more than 18 or it will take forever) : ");
            int maxZoom = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());
            Console.Write("Output Directory: ");
            string outputDir = Console.ReadLine() ?? throw new InvalidOperationException();
            Directory.CreateDirectory(outputDir);


            using HttpClient httpClient = new HttpClient();
            var semaphore = new SemaphoreSlim(MaxConcurrentRequests);
            var tasks = new ConcurrentBag<Task>();

            for (int zoom = minZoom; zoom <= maxZoom; zoom++)
            {
                var (xMin, yMax) = LatLonToTileXY(minLat, minLon, zoom);
                var (xMax, yMin) = LatLonToTileXY(maxLat, maxLon, zoom);

                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        string quadkey = TileXYToQuadKey(x, y, zoom);
                        string url = $"https://s3-nox-prd-processing-soc-tli-v2-use1.s3.amazonaws.com/img/t_{quadkey}.png";
                        string urlb = $"https://s3-nox-prd-processing-soc-tli-v2-use1.s3.amazonaws.com/img/b_{quadkey}.png";

                        string dirPath = Path.Combine(outputDir, "contour", zoom.ToString(), x.ToString());
                        string dirBPath = Path.Combine(outputDir, "shade", zoom.ToString(), x.ToString());
                        string filePath = Path.Combine(dirPath, $"{y}.png");
                        string fileBPath = Path.Combine(dirBPath, $"{y}.png");

                        if (File.Exists(fileBPath))
                        {
                            continue;
                        }

                        await semaphore.WaitAsync(); 
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var data = await httpClient.GetByteArrayAsync(url);
                                Directory.CreateDirectory(dirPath);
                                await File.WriteAllBytesAsync(filePath, data);

                                var bData = await httpClient.GetByteArrayAsync(urlb);
                                Directory.CreateDirectory(dirBPath);
                                await File.WriteAllBytesAsync(fileBPath, bData);

                                Console.WriteLine($"Downloaded {zoom}/{x}/{y}");
                            }
                            catch (HttpRequestException ex)
                            {
                                Console.WriteLine($"Failed {zoom}/{x}/{y}: {ex.Message}");
                            }
                            finally
                            {
                                semaphore.Release(); 
                            }
                        }));
                    }
                }
            }

            await Task.WhenAll(tasks);
            GenerateTileJson(minLon, minLat, maxLon, maxLat, minZoom, maxZoom, outputDir);
            Console.WriteLine("Done");
        }

        static void GenerateTileJson(
            double minLon, double minLat,
            double maxLon, double maxLat,
            int minZoom, int maxZoom,
            string outputDir)
        {
            // Compute center point
            double centerLon = (minLon + maxLon) / 2.0;
            double centerLat = (minLat + maxLat) / 2.0;
            int centerZoom = (minZoom + maxZoom) / 2;

            
            var metadata = new
            {
                // TileJSON version (optional)
                tilejson = "2.2.0",
                // Identifier of the tileset
                name = "cmap",
                description = "Silkeborg depth",
                version = "1.0.0",
                attribution = string.Empty,
                // Use tms scheme for SignalK offline charts
                scheme = "tms",
                type = "tilelayer",
                // File format of tiles
                format = "png",
                // URL template, relative to metadata.json location
                tiles = new[] { "./{z}/{x}/{y}.png" },
                // Zoom extents
                minzoom = minZoom,
                maxzoom = maxZoom,
                // Geographic bounds: [west, south, east, north]
                bounds = new[] { minLon, minLat, maxLon, maxLat },
                // Default center [lon, lat, zoom]
                center = new object[] { centerLon, centerLat, centerZoom }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(metadata, options);
            File.WriteAllText(Path.Combine(outputDir, "metadata.json"), json);

        }



        static (int tileX, int tileY) LatLonToTileXY(double lat, double lon, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            int n = 1 << zoom;
            int x = (int)((lon + 180.0) / 360.0 * n);
            int y = (int)((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
            return (x, y);
        }

        static string TileXYToQuadKey(int tileX, int tileY, int levelOfDetail)
        {
            char[] quadKey = new char[levelOfDetail];
            for (int i = levelOfDetail; i > 0; i--)
            {
                int digit = 0;
                int mask = 1 << (i - 1);
                if ((tileX & mask) != 0) digit += 1;
                if ((tileY & mask) != 0) digit += 2;
                quadKey[levelOfDetail - i] = (char)('0' + digit);
            }
            return new string(quadKey);
        }
    }
}
