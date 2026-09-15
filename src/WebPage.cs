namespace V380Decoder.src
{
    public class WebPage
    {
        public static string GetHtml(bool enableMjpeg)
        {
            string sectionMjpeg = enableMjpeg ? @"
                <section class='section'>
                    <h2>Live Stream</h2>
                    <img class='live-frame' alt='Loading stream...' src='/mjpeg'>
                </section>" : "";

            return @"<!doctype html>
<html lang='en'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>V380 Control Panel</title>
    <style>
        * { box-sizing: border-box; }
        body { margin: 0; min-height: 100vh; padding: 20px; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif; color: #1f2937; background: linear-gradient(135deg, #abdfff, #0396ff); }
        .container { width: min(900px, 100%); margin: 0 auto; padding: 28px; border-radius: 20px; background: white; box-shadow: 0 20px 60px rgba(0,0,0,.25); }
        h1 { margin: 0 0 26px; text-align: center; font-size: 28px; }
        h2 { margin: 0 0 14px; color: #64748b; font-size: 15px; letter-spacing: .08em; text-transform: uppercase; }
        .section { margin-bottom: 30px; }
        .live-frame { width: 100%; aspect-ratio: 16/9; object-fit: cover; border-radius: 14px; background: #0f172a; }
        .ptz-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; max-width: 480px; margin: auto; }
        button { border: 0; border-radius: 9px; padding: 12px 14px; cursor: pointer; font: inherit; font-weight: 650; transition: transform .15s, opacity .15s; }
        button:hover { transform: translateY(-1px); }
        button:disabled { cursor: wait; opacity: .55; transform: none; }
        .primary { color: white; background: #0284c7; }
        .ptz-direction { touch-action: none; user-select: none; }
        .go { color: white; background: #16a34a; }
        .remove { color: #991b1b; background: #fee2e2; }
        .secondary { color: white; background: #64748b; }
        .button-row { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
        .image-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }
        .preset-note { margin: -5px 0 14px; color: #64748b; font-size: 13px; line-height: 1.45; }
        .preset-list { display: grid; gap: 9px; }
        .preset-row { display: grid; grid-template-columns: 58px minmax(130px,1fr) auto auto auto; gap: 8px; align-items: center; padding: 10px; border: 1px solid #e2e8f0; border-radius: 11px; }
        .slot { font-weight: 750; color: #475569; text-align: center; }
        input { min-width: 0; width: 100%; border: 1px solid #cbd5e1; border-radius: 8px; padding: 11px; font: inherit; }
        input:focus { outline: 2px solid #7dd3fc; border-color: #0284c7; }
        .status { position: sticky; bottom: 12px; display: none; margin-top: 18px; padding: 12px 14px; border-radius: 10px; text-align: center; font-size: 14px; box-shadow: 0 5px 20px rgba(0,0,0,.15); }
        .status.show { display: block; }
        .status.success { color: #14532d; background: #dcfce7; }
        .status.error { color: #7f1d1d; background: #fee2e2; }
        @media (max-width: 650px) {
            body { padding: 8px; }
            .container { padding: 18px 14px; border-radius: 14px; }
            .preset-row { grid-template-columns: 48px 1fr 1fr; }
            .preset-row input { grid-column: 2 / 4; grid-row: 1; }
            .preset-row .slot { grid-row: 1; }
            .preset-row button { padding: 10px 6px; }
            .button-row, .image-row { grid-template-columns: repeat(2, 1fr); }
        }
    </style>
</head>
<body>
    <main class='container'>
        <h1>V380 Control</h1>" + sectionMjpeg + @"
        <section class='section'>
            <h2>PTZ Control</h2>
            <p class='preset-note'>Press and hold a direction button, then release it to stop.</p>
            <div class='ptz-grid'>
                <span></span><button class='primary ptz-direction' data-direction='up'>Up</button><span></span>
                <button class='primary ptz-direction' data-direction='left'>Left</button>
                <button class='secondary' id='ptz-stop'>Stop</button>
                <button class='primary ptz-direction' data-direction='right'>Right</button>
                <span></span><button class='primary ptz-direction' data-direction='down'>Down</button><span></span>
            </div>
        </section>

        <section class='section'>
            <h2>Camera Presets</h2>
            <p class='preset-note'>Save stores the camera's current physical view in the numbered slot and stores its name in V380Decoder. Remove clears only the local name; the camera slot remains until it is overwritten.</p>
            <div id='preset-list' class='preset-list'></div>
        </section>

        <section class='section'>
            <h2>Light Control</h2>
            <div class='button-row'>
                <button class='go' onclick=""command('/api/light/on')"">On</button>
                <button class='go' onclick=""command('/api/light/off')"">Off</button>
                <button class='go' onclick=""command('/api/light/auto')"">Auto</button>
            </div>
        </section>

        <section class='section'>
            <h2>Image Mode</h2>
            <div class='image-row'>
                <button class='secondary' onclick=""command('/api/image/color')"">Color</button>
                <button class='secondary' onclick=""command('/api/image/bw')"">B&amp;W</button>
                <button class='secondary' onclick=""command('/api/image/auto')"">Auto</button>
                <button class='secondary' onclick=""command('/api/image/flip')"">Flip</button>
            </div>
        </section>
        <div id='status' class='status' role='status' aria-live='polite'></div>
    </main>

    <script>
        const statusBox = document.getElementById('status');
        let ptzSession = 0;
        function showStatus(message, isError) {
            statusBox.textContent = message;
            statusBox.className = 'status show ' + (isError ? 'error' : 'success');
        }
        async function request(url, options) {
            const response = await fetch(url, options || {});
            const text = await response.text();
            let data = null;
            if (text) { try { data = JSON.parse(text); } catch { data = null; } }
            if (!response.ok) throw new Error((data && (data.error || data.detail || data.title)) || ('Request failed (' + response.status + ')'));
            return data;
        }
        async function command(url) {
            try { await request(url, { method: 'POST' }); showStatus('Command sent.', false); }
            catch (error) { showStatus(error.message, true); }
        }
        function delay(milliseconds) { return new Promise(function(resolve) { setTimeout(resolve, milliseconds); }); }
        async function startPtz(direction) {
            if (ptzSession !== 0) return;
            const session = Date.now();
            ptzSession = session;
            try {
                while (ptzSession === session) {
                    await request('/api/ptz/' + direction, { method: 'POST' });
                    await delay(100);
                }
            } catch (error) {
                ptzSession = 0;
                showStatus(error.message, true);
            }
        }
        async function stopPtz() {
            ptzSession = 0;
            try { await request('/api/ptz/stop', { method: 'POST' }); }
            catch (error) { showStatus(error.message, true); }
        }
        document.querySelectorAll('.ptz-direction').forEach(function(button) {
            button.addEventListener('pointerdown', function(event) {
                event.preventDefault();
                button.setPointerCapture(event.pointerId);
                startPtz(button.dataset.direction);
            });
            button.addEventListener('pointerup', stopPtz);
            button.addEventListener('pointercancel', stopPtz);
            button.addEventListener('lostpointercapture', stopPtz);
            button.addEventListener('contextmenu', function(event) { event.preventDefault(); });
            button.addEventListener('keydown', function(event) {
                if ((event.key === 'Enter' || event.key === ' ') && !event.repeat) { event.preventDefault(); startPtz(button.dataset.direction); }
            });
            button.addEventListener('keyup', function(event) {
                if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); stopPtz(); }
            });
        });
        document.getElementById('ptz-stop').addEventListener('click', stopPtz);
        window.addEventListener('blur', stopPtz);
        function makeButton(label, className, handler) {
            const button = document.createElement('button'); button.textContent = label; button.className = className; button.addEventListener('click', handler); return button;
        }
        async function savePreset(slot, input, button) {
            const name = input.value.trim();
            if (!name) { showStatus('Enter a name for slot ' + slot + '.', true); input.focus(); return; }
            button.disabled = true;
            try {
                await request('/api/ptz/native-presets/' + slot, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: name }) });
                showStatus('Saved “' + name + '” to camera slot ' + slot + '.', false);
            } catch (error) { showStatus(error.message, true); }
            finally { button.disabled = false; }
        }
        async function goPreset(slot, button) {
            button.disabled = true;
            try { await request('/api/ptz/native-presets/' + slot + '/goto', { method: 'POST' }); showStatus('Moving directly to camera slot ' + slot + '.', false); }
            catch (error) { showStatus(error.message, true); }
            finally { button.disabled = false; }
        }
        async function removePreset(slot, input, button) {
            if (!confirm('Remove the local name for slot ' + slot + '? The camera position itself will remain stored.')) return;
            button.disabled = true;
            try { await request('/api/ptz/native-presets/' + slot, { method: 'DELETE' }); input.value = ''; showStatus('Removed the local name for slot ' + slot + '. The camera slot was retained.', false); }
            catch (error) { showStatus(error.message, true); }
            finally { button.disabled = false; }
        }
        function renderPresets(presets) {
            const list = document.getElementById('preset-list'); list.replaceChildren();
            presets.forEach(function(preset) {
                const row = document.createElement('div'); row.className = 'preset-row';
                const label = document.createElement('div'); label.className = 'slot'; label.textContent = '#' + preset.slot;
                const input = document.createElement('input'); input.maxLength = 64; input.placeholder = 'Preset name'; input.value = preset.name || '';
                const save = makeButton('Save', 'primary', function() { savePreset(preset.slot, input, save); });
                const go = makeButton('Go', 'go', function() { goPreset(preset.slot, go); });
                const remove = makeButton('Remove', 'remove', function() { removePreset(preset.slot, input, remove); });
                row.append(label, input, save, go, remove); list.appendChild(row);
            });
        }
        async function loadPresets() {
            try { renderPresets(await request('/api/ptz/native-presets')); }
            catch (error) { showStatus('Could not load presets: ' + error.message, true); }
        }
        loadPresets();
    </script>
</body>
</html>";
        }
    }
}
