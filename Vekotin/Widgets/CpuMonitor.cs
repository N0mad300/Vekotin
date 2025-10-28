namespace Vekotin.Widgets
{
    class CpuMonitorWidget : WidgetExample
    {
        public override string FolderName => "Vekotin - CPU Monitor";

        public override Dictionary<string, string> Files => new()
        {
            /// <summary>
            /// index.html
            /// </summary>

            ["index.html"] = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>CPU Monitor</title>
    <link rel=""stylesheet"" href=""styles.css"">
</head>
<body id=""drag-handle"">
    <div class=""widget-container"">
        <div id=""cpu-name"" class=""cpu-name"">Loading...</div>

        <div class=""main-display"">
            <span id=""cpu-percent"">...</span>
            <span class=""unit"">%</span>
        </div>

        <div class=""details"">
            <div class=""detail-item"">
                <span class=""label"">Speed</span>
                <span id=""cpu-speed"" class=""value"">... GHz</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Cores</span>
                <span id=""cpu-cores"" class=""value"">...</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Threads</span>
                <span id=""cpu-threads"" class=""value"">...</span>
            </div>
        </div>
    </div>

    <script src=""script.js""></script>
</body>
</html>",

            /// <summary>
            /// script.json
            /// </summary>

            ["script.js"] = @"document.addEventListener('DOMContentLoaded', () => {
    const cpuNameElement = document.getElementById('cpu-name');
    const cpuPercentElement = document.getElementById('cpu-percent');
    const cpuSpeedElement = document.getElementById('cpu-speed');
    const cpuCoresElement = document.getElementById('cpu-cores');
    const cpuThreadsElement = document.getElementById('cpu-threads');

    const cpuBridge = window.chrome?.webview?.hostObjects?.cpu;

    async function getStaticCpuInfo() {
        if (!cpuBridge) {
            cpuNameElement.textContent = ""Bridge Unavailable"";
            return;
        }
        try {
            const [name, coreCount, logicalCount] = await Promise.all([
                cpuBridge.GetName(),
                cpuBridge.GetCoreCount(),
                cpuBridge.GetLogicalProcessorCount()
            ]);

            cpuNameElement.textContent = name;
            cpuCoresElement.textContent = coreCount;
            cpuThreadsElement.textContent = logicalCount;
        } catch (error) {
            console.error(""Error fetching static CPU info:"", error);
            cpuNameElement.textContent = ""Error loading details"";
        }
    }

    async function updateDynamicCpuInfo() {
        if (!cpuBridge) {
            cpuPercentElement.textContent = ""—"";
            cpuSpeedElement.textContent = ""—"";
            return;
        }
        try {
            const [usage, speedMhz] = await Promise.all([
                cpuBridge.GetUsage(),
                cpuBridge.GetCurrentClockSpeed()
            ]);

            cpuPercentElement.textContent = Math.round(usage);
            cpuSpeedElement.textContent = (speedMhz / 1000).toFixed(2) + "" GHz"";
        } catch (error) {
            console.error(""Error fetching dynamic CPU info:"", error);
            cpuPercentElement.textContent = ""—"";
            cpuSpeedElement.textContent = ""—"";
        }
    }

    getStaticCpuInfo();
    updateDynamicCpuInfo();
    setInterval(updateDynamicCpuInfo, 2000);
});",

            /// <summary>
            /// styles.css
            /// </summary>

            ["styles.css"] = @"* {
    margin: 0;
    padding: 0;
    box-sizing: border-box;
}

body {
    -webkit-app-region: drag;
    font-family: 'Segoe UI', 'Inter', sans-serif;
    background-color: transparent;
    color: #ffffff;
    user-select: none;
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 100vh;
}

button, a, input, textarea {
    -webkit-app-region: no-drag;
}

.widget-container {
    background: rgba(20, 20, 25, 0.75);
    backdrop-filter: blur(20px) saturate(180%);
    -webkit-backdrop-filter: blur(20px) saturate(180%);
    border: 1px solid rgba(255, 255, 255, 0.08);
    border-radius: 12px;
    padding: 20px 24px;
    min-width: 240px;
}

.cpu-name {
    font-size: 11px;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.5);
    margin-bottom: 16px;
    text-align: center;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    letter-spacing: 0.3px;
}

.main-display {
    display: flex;
    align-items: baseline;
    justify-content: center;
    margin-bottom: 20px;
    gap: 4px;
}

#cpu-percent {
    font-size: 56px;
    font-weight: 300;
    line-height: 1;
    letter-spacing: -2px;
}

.unit {
    font-size: 20px;
    font-weight: 400;
    color: rgba(255, 255, 255, 0.6);
}

.details {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 16px;
    padding-top: 16px;
    border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.detail-item {
    text-align: center;
}

.detail-item .label {
    display: block;
    font-size: 9px;
    color: rgba(255, 255, 255, 0.4);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 6px;
    font-weight: 500;
}

.detail-item .value {
    font-size: 13px;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.9);
}",

            /// <summary>
            /// widget.json
            /// </summary>

            ["widget.json"] = ToJson(new
            {
                Name = "Vekotin - CPU Monitor",
                Author = "N0mad300",
                Version = "1.0.0",
                License = "MIT",
                Description = "Monitor CPU usage",
                Width = 255,
                Height = 210,
                Bridges = new string[] { "CPU" }
            })
        };
    }
}
