namespace Vekotin.Widgets
{
    class RamMonitorWidget : WidgetExample
    {
        public override string FolderName => "Vekotin - RAM Monitor";

        public override Dictionary<string, string> Files => new()
        {
            /// <summary>
            /// index.html
            /// </summary>

            ["index.html"] = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>RAM Monitor</title>
    <link rel=""stylesheet"" href=""styles.css"">
</head>
<body id=""drag-handle"">
    <div class=""widget-container"">
        <div class=""ram-label"">Memory Usage</div>

        <div class=""main-display"">
            <span id=""ram-percent"">...</span>
            <span class=""unit"">%</span>
        </div>

        <div class=""details"">
            <div class=""detail-item"">
                <span class=""label"">Used</span>
                <span id=""ram-used"" class=""value"">... GB</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Total</span>
                <span id=""ram-total"" class=""value"">... GB</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Free</span>
                <span id=""ram-free"" class=""value"">... GB</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Committed</span>
                <span id=""ram-committed"" class=""value"">... GB</span>
            </div>
        </div>
    </div>

    <script src=""script.js""></script>
</body>
</html>",

            /// <summary>
            /// script.json
            /// </summary>

            ["script.js"] = @"// Cache DOM elements
const ramPercentElement = document.getElementById('ram-percent');
const ramUsedElement = document.getElementById('ram-used');
const ramTotalElement = document.getElementById('ram-total');
const ramFreeElement = document.getElementById('ram-free');
const ramCommittedElement = document.getElementById('ram-committed');

const ramBridge = window.chrome?.webview?.hostObjects?.ram;

// Track previous values to avoid unnecessary DOM updates
let lastPercent = '';
let lastUsed = '';
let lastTotal = '';
let lastFree = '';
let lastCommitted = '';

function formatGB(megabytes) {
    // Bridge returns MB, convert to GB
    return (megabytes / 1024).toFixed(1) + ' GB';
}

async function getStaticRamInfo() {
    if (!ramBridge) {
        ramTotalElement.textContent = ""Bridge Unavailable"";
        return;
    }
    try {
        const totalMemory = await ramBridge.GetTotalMemory();
        const totalFormatted = formatGB(totalMemory);
        ramTotalElement.textContent = totalFormatted;
        lastTotal = totalFormatted;
    } catch (error) {
        console.error(""Error fetching total RAM:"", error);
        ramTotalElement.textContent = ""Error"";
    }
}

async function updateDynamicRamInfo() {
    if (!ramBridge) {
        ramPercentElement.textContent = ""—"";
        ramUsedElement.textContent = ""—"";
        ramFreeElement.textContent = ""—"";
        ramCommittedElement.textContent = ""—"";
        return;
    }
    try {
        const [usage, usedMemory, freeMemory, committedMemory] = await Promise.all([
            ramBridge.GetUsage(),
            ramBridge.GetUsedMemory(),
            ramBridge.GetFreeMemory(),
            ramBridge.GetCommittedMemory()
        ]);

        const percentStr = Math.round(usage).toString();
        const usedStr = formatGB(usedMemory);
        const freeStr = formatGB(freeMemory);
        const committedStr = formatGB(committedMemory);

        if (percentStr !== lastPercent) {
            ramPercentElement.textContent = percentStr;
            lastPercent = percentStr;
        }

        if (usedStr !== lastUsed) {
            ramUsedElement.textContent = usedStr;
            lastUsed = usedStr;
        }

        if (freeStr !== lastFree) {
            ramFreeElement.textContent = freeStr;
            lastFree = freeStr;
        }

        if (committedStr !== lastCommitted) {
            ramCommittedElement.textContent = committedStr;
            lastCommitted = committedStr;
        }

    } catch (error) {
        console.error(""Error fetching dynamic RAM info:"", error);
        ramPercentElement.textContent = ""—"";
        ramUsedElement.textContent = ""—"";
        ramFreeElement.textContent = ""—"";
        ramCommittedElement.textContent = ""—"";
    }
}

getStaticRamInfo();
updateDynamicRamInfo();
setInterval(updateDynamicRamInfo, 1000);",

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
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Inter', sans-serif;
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
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
}

.ram-label {
    font-size: 11px;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.5);
    margin-bottom: 16px;
    text-align: center;
    letter-spacing: 0.3px;
}

.main-display {
    display: flex;
    align-items: baseline;
    justify-content: center;
    margin-bottom: 20px;
    gap: 4px;
}

#ram-percent {
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
    grid-template-columns: repeat(2, 1fr);
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
                Name = "Vekotin - RAM Monitor",
                Author = "N0mad300",
                Version = "1.0.0",
                License = "MIT",
                Description = "Monitor RAM usage",
                Width = 240,
                Height = 260,
                Bridges = new string[] { "RAM" }
            })
        };
    }
}
