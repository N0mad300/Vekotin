namespace Vekotin.Widgets
{
    class DiskMonitorWidget : WidgetExample
    {
        public override string FolderName => "Vekotin - Disk Monitor";

        public override Dictionary<string, string> Files => new()
        {
            /// <summary>
            /// index.html
            /// </summary>

            ["index.html"] = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Disk Monitor</title>
    <link rel=""stylesheet"" href=""styles.css"">
</head>
<body id=""drag-handle"">
    <div class=""widget-container"">
        <div class=""header"">Disk Usage</div>
        <div id=""drives-container"" class=""drives-container"">
            <div class=""no-drives"">Loading drives...</div>
        </div>
    </div>

    <script src=""script.js""></script>
</body>
</html>",

            /// <summary>
            /// script.json
            /// </summary>

            ["script.js"] = @"const drivesContainer = document.getElementById('drives-container');
const diskBridge = window.chrome?.webview?.hostObjects?.disk;

const driveElements = new Map();
const driveCache = new Map();

function formatGB(megabytes) {
    return (megabytes / 1024).toFixed(1) + ' GB';
}

function formatSpeed(bytesPerSec) {
    const mbps = bytesPerSec / (1024 * 1024);
    if (mbps < 1) return '0 MB/s';
    return mbps.toFixed(1) + ' MB/s';
}

function createDriveElement(driveLetter) {
    const driveItem = document.createElement('div');
    driveItem.className = 'drive-item';
    driveItem.innerHTML = `
        <div class=""drive-header"">
            <div class=""drive-name"">
                <span class=""drive-letter"">${driveLetter}</span>
                <span class=""drive-type"">HDD</span>
            </div>
            <div class=""drive-usage"">0%</div>
        </div>
        <div class=""progress-bar"">
            <div class=""progress-fill"" style=""width: 0%""></div>
        </div>
        <div class=""drive-details"">
            <div class=""detail-item"">
                <span class=""label"">Used</span>
                <span class=""value drive-used"">0 GB</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Free</span>
                <span class=""value drive-free"">0 GB</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Total</span>
                <span class=""value drive-total"">0 GB</span>
            </div>
        </div>
    `;

    const elements = {
        container: driveItem,
        usage: driveItem.querySelector('.drive-usage'),
        progressFill: driveItem.querySelector('.progress-fill'),
        type: driveItem.querySelector('.drive-type'),
        used: driveItem.querySelector('.drive-used'),
        free: driveItem.querySelector('.drive-free'),
        total: driveItem.querySelector('.drive-total')
    };

    driveElements.set(driveLetter, elements);
    return driveItem;
}

async function initializeDrives() {
    if (!diskBridge) {
        drivesContainer.innerHTML = '<div class=""no-drives"">Bridge Unavailable</div>';
        return;
    }

    try {
        const driveLetters = await diskBridge.GetDriveLetters();
        
        if (!driveLetters || driveLetters.length === 0) {
            drivesContainer.innerHTML = '<div class=""no-drives"">No drives found</div>';
            return;
        }

        drivesContainer.innerHTML = '';

        for (const driveLetter of driveLetters) {
            const driveElement = createDriveElement(driveLetter);
            drivesContainer.appendChild(driveElement);

            const [isSSD, totalSpace] = await Promise.all([
                diskBridge.IsDriveSSD(driveLetter),
                diskBridge.GetTotalSpace(driveLetter)
            ]);

            const elements = driveElements.get(driveLetter);
            const totalFormatted = formatGB(totalSpace);
            
            elements.type.textContent = isSSD ? 'SSD' : 'HDD';
            if (isSSD) elements.type.classList.add('ssd');
            elements.total.textContent = totalFormatted;

            driveCache.set(driveLetter, {
                total: totalFormatted,
                lastUsage: '',
                lastUsed: '',
                lastFree: ''
            });
        }

        updateDriveInfo();
    } catch (error) {
        console.error('Error initializing drives:', error);
        drivesContainer.innerHTML = '<div class=""no-drives"">Error loading drives</div>';
    }
}

function sendSizeToParent() {
    const container = document.querySelector('.widget-container');
    if (!container) return;
    
    const style = window.getComputedStyle(container);
    
    // Width and height including content + padding + border
    const width = container.offsetWidth;
    const height = container.offsetHeight;
    
    // Include margins
    const marginTop = parseFloat(style.marginTop);
    const marginBottom = parseFloat(style.marginBottom);
    const marginLeft = parseFloat(style.marginLeft);
    const marginRight = parseFloat(style.marginRight);
    
    const totalWidth = width + marginLeft + marginRight;
    const totalHeight = height + marginTop + marginBottom;
    
    // Send a message to C# with the size
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage({
            type: 'resize',
            width: totalWidth,
            height: totalHeight
        });
    }
}

async function updateDriveInfo() {
    if (!diskBridge) return;

    try {
        const driveLetters = await diskBridge.GetDriveLetters();

        for (const driveLetter of driveLetters) {
            const elements = driveElements.get(driveLetter);
            const cache = driveCache.get(driveLetter);
            if (!elements || !cache) continue;

            const [usage, usedSpace, freeSpace] = await Promise.all([
                diskBridge.GetUsage(driveLetter),
                diskBridge.GetUsedSpace(driveLetter),
                diskBridge.GetFreeSpace(driveLetter)
            ]);

            const usageStr = Math.round(usage) + '%';
            const usedStr = formatGB(usedSpace);
            const freeStr = formatGB(freeSpace);

            if (usageStr !== cache.lastUsage) {
                elements.usage.textContent = usageStr;
                elements.progressFill.style.width = Math.round(usage) + '%';
                cache.lastUsage = usageStr;
            }

            if (usedStr !== cache.lastUsed) {
                elements.used.textContent = usedStr;
                cache.lastUsed = usedStr;
            }

            if (freeStr !== cache.lastFree) {
                elements.free.textContent = freeStr;
                cache.lastFree = freeStr;
            }
        }
    } catch (error) {
        console.error('Error updating drive info:', error);
    }
}

initializeDrives();
setInterval(updateDriveInfo, 3000);

const container = document.querySelector('.widget-container');
const resizeObserver = new ResizeObserver(() => {
    sendSizeToParent();
});

resizeObserver.observe(container);",

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
    min-width: 280px;
    max-width: 400px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
}

.header {
    font-size: 11px;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.5);
    margin-bottom: 16px;
    text-align: center;
    letter-spacing: 0.3px;
}

.drives-container {
    display: flex;
    flex-direction: column;
    gap: 12px;
}

.drive-item {
    padding: 12px;
    background: rgba(255, 255, 255, 0.03);
    border-radius: 8px;
    border: 1px solid rgba(255, 255, 255, 0.05);
}

.drive-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
}

.drive-name {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 13px;
    font-weight: 600;
    color: rgba(255, 255, 255, 0.9);
}

.drive-type {
    font-size: 9px;
    padding: 2px 6px;
    background: rgba(100, 150, 255, 0.2);
    border-radius: 4px;
    color: rgba(150, 200, 255, 0.9);
    text-transform: uppercase;
    font-weight: 600;
    letter-spacing: 0.3px;
}

.drive-type.ssd {
    background: rgba(100, 255, 150, 0.2);
    color: rgba(150, 255, 200, 0.9);
}

.drive-usage {
    font-size: 24px;
    font-weight: 300;
    letter-spacing: -1px;
    color: rgba(255, 255, 255, 0.95);
}

.progress-bar {
    width: 100%;
    height: 4px;
    background: rgba(255, 255, 255, 0.1);
    border-radius: 2px;
    overflow: hidden;
    margin: 8px 0;
}

.progress-fill {
    height: 100%;
    background: linear-gradient(90deg, rgba(100, 150, 255, 0.8), rgba(150, 100, 255, 0.8));
    border-radius: 2px;
    transition: width 0.3s ease;
}

.drive-details {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 8px;
    margin-top: 8px;
}

.detail-item {
    text-align: center;
}

.detail-item .label {
    display: block;
    font-size: 8px;
    color: rgba(255, 255, 255, 0.4);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 4px;
    font-weight: 500;
}

.detail-item .value {
    font-size: 11px;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.85);
}

.no-drives {
    text-align: center;
    padding: 20px;
    color: rgba(255, 255, 255, 0.5);
    font-size: 12px;
}",

            /// <summary>
            /// widget.json
            /// </summary>

            ["widget.json"] = ToJson(new
            {
                Name = "Vekotin - Disk Monitor",
                Author = "N0mad300",
                Version = "1.0.0",
                License = "MIT",
                Description = "Monitor Disk usage",
                Width = 280,
                Height = 187,
                Bridges = new string[] { "Disk" }
            })
        };
    }
}
