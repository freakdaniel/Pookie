namespace Pookie.App.Browser;

internal static class EmbeddedLoginPage
{
    // An SPA's document can finish loading while its authentication root is still
    // a logo or an empty shell. Wait for controls before capturing the page.
    internal const string ReadyScript = """
        (() => {
          let sent = false;
          function schedule(callback, delay = 100) {
            let called = false, frame = 0;
            const timer = setTimeout(run, delay);
            function run() {
              if (called) return;
              called = true; clearTimeout(timer);
              if (frame) cancelAnimationFrame(frame);
              callback();
            }
            if (typeof requestAnimationFrame === 'function') frame = requestAnimationFrame(run);
          }
          function hasControls() {
            const root = document.getElementById('app') || document.body;
            return root && Array.from(root.querySelectorAll('button,input:not([type="hidden"]),textarea,select,a[href],[role="button"],[role="alert"],iframe'))
              .some(element => {
                const rect = element.getBoundingClientRect();
                if (rect.width <= 0 || rect.height <= 0) return false;
                let opacity = 1;
                for (let node = element; node; node = node.parentElement) {
                  const style = getComputedStyle(node);
                  if (style.visibility === 'hidden' || style.display === 'none') return false;
                  let alpha = Number(style.opacity);
                  // WebKit can pause CSS transitions in the offscreen staging
                  // view too. A control fading in is ready to be revealed; a
                  // permanently transparent SPA placeholder is not.
                  for (const animation of node.getAnimations?.() || []) {
                    if (animation.playState !== 'running') continue;
                    const frames = animation.effect?.getKeyframes() || [];
                    const target = frames[frames.length - 1]?.opacity;
                    if (target !== undefined) alpha = Math.max(alpha, Number(target));
                  }
                  opacity *= alpha;
                }
                return opacity >= .99;
              });
          }
          function check() {
            if (sent) return;
            if (document.readyState === 'loading' || !hasControls()) { schedule(check); return; }
            // Offscreen native views may suspend animation frames. Keep the
            // DOM signal independent of painting; preview capture is bounded separately.
            schedule(() => schedule(() => {
              if (!hasControls()) { schedule(check); return; }
              sent = true;
              window.__pookiePost({id:'pookie:web-api',version:1,data:{kind:'ready'}});
            }, 50), 50);
          }
          setTimeout(check, 0);
        })();
        """;
}
