namespace Vekotin.Widgets
{
    class ClockWidget : WidgetExample
    {
        public override string FolderName => "Vekotin - Clock";

        public override Dictionary<string, string> Files => new()
        {
            /// <summary>
            /// index.html
            /// </summary>

            ["index.html"] = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Clock Widget</title>
    <link rel=""stylesheet"" href=""styles.css"">
</head>
<body id=""drag-handle"">
    <div class=""widget-container"">
        <div id=""date"" class=""date"">Loading...</div>

        <div class=""time-display"">
            <span id=""time"">...</span>
            <span id=""period"" class=""period""></span>
        </div>

        <div class=""details"">
            <div class=""detail-item"">
                <span class=""label"">Day</span>
                <span id=""day"" class=""value"">...</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Week</span>
                <span id=""week"" class=""value"">...</span>
            </div>
            <div class=""detail-item"">
                <span class=""label"">Timezone</span>
                <span id=""timezone"" class=""value"">...</span>
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
const timeEl = document.getElementById('time');
const periodEl = document.getElementById('period');
const dateEl = document.getElementById('date');
const dayEl = document.getElementById('day');
const weekEl = document.getElementById('week');
const timezoneEl = document.getElementById('timezone');

// Cache locale and timezone info
const locale = navigator.language || 'en-US';
const use12Hour = new Intl.DateTimeFormat(locale, { 
    hour: 'numeric', 
    hour12: undefined 
}).resolvedOptions().hour12;

const dateFormatter = new Intl.DateTimeFormat('en-US', { 
    weekday: 'long', 
    year: 'numeric', 
    month: 'long', 
    day: 'numeric' 
});

const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const tzAbbr = timezone.split('/').pop().replace('_', ' ');
timezoneEl.textContent = tzAbbr;

function getWeekNumber(date) {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const dayNum = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    return Math.ceil((((d - yearStart) / 86400000) + 1) / 7);
}

// Track previous values to avoid unnecessary DOM updates
let lastTimeStr = '';
let lastDateStr = '';
let lastDayOfYear = -1;
let lastWeek = -1;

function updateClock() {
    const now = new Date();
    
    // Time
    let hours = now.getHours();
    const minutes = now.getMinutes();
    
    let timeString, periodString;
    
    if (use12Hour) {
        const isPM = hours >= 12;
        hours = hours % 12 || 12;
        timeString = `${hours}:${minutes.toString().padStart(2, '0')}`;
        periodString = isPM ? 'PM' : 'AM';
    } else {
        timeString = `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}`;
        periodString = '';
    }
    
    if (timeString !== lastTimeStr) {
        timeEl.textContent = timeString;
        periodEl.textContent = periodString;
        lastTimeStr = timeString;
    }
    
    // Date
    const dateString = dateFormatter.format(now);
    if (dateString !== lastDateStr) {
        dateEl.textContent = dateString;
        lastDateStr = dateString;
    }
    
    // Day of year
    const start = new Date(now.getFullYear(), 0, 0);
    const diff = now - start;
    const dayOfYear = Math.floor(diff / 86400000);
    if (dayOfYear !== lastDayOfYear) {
        dayEl.textContent = dayOfYear;
        lastDayOfYear = dayOfYear;
    }
    
    // Week number
    const weekNumber = getWeekNumber(now);
    if (weekNumber !== lastWeek) {
        weekEl.textContent = weekNumber;
        lastWeek = weekNumber;
    }
}

// Update immediately
updateClock();

// Update every second
setInterval(updateClock, 1000);",

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

.date {
    font-size: 11px;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.5);
    margin-bottom: 16px;
    text-align: center;
    letter-spacing: 0.3px;
}

.time-display {
    display: flex;
    align-items: baseline;
    justify-content: center;
    margin-bottom: 20px;
    gap: 4px;
}

#time {
    font-size: 56px;
    font-weight: 300;
    line-height: 1;
    letter-spacing: -2px;
}

.period {
    font-size: 20px;
    font-weight: 400;
    color: rgba(255, 255, 255, 0.6);
    margin-left: 4px;
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
                Name = "Vekotin - Clock",
                Author = "N0mad300",
                Version = "1.0.0",
                License = "MIT",
                Description = "Display time and date",
                Width = 250,
                Height = 210,
                Bridges = new string[] { }
            })
        };
    }
}
