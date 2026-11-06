namespace Pookie.App.Browser;

internal static class EmbeddedLoginPage
{
    // An SPA's document can finish loading while its authentication root is still
    // a logo or an empty shell. Wait for visible controls and their painted frame.
    internal const string ReadyScript = """
        (() => {
          let sent = false;
          function hasControls() {
            const root = document.getElementById('app') || document.body;
            return root && Array.from(root.querySelectorAll('button,input:not([type="hidden"]),[role="button"],[role="alert"],iframe'))
              .some(element => {
                const rect = element.getBoundingClientRect();
                if (rect.width <= 0 || rect.height <= 0) return false;
                let opacity = 1;
                for (let node = element; node; node = node.parentElement) {
                  const style = getComputedStyle(node);
                  if (style.visibility === 'hidden' || style.display === 'none') return false;
                  opacity *= Number(style.opacity);
                }
                return opacity >= .99;
              });
          }
          function check() {
            if (sent) return;
            if (document.readyState === 'loading' || !hasControls()) { requestAnimationFrame(check); return; }
            requestAnimationFrame(() => requestAnimationFrame(() => {
              if (!hasControls()) { requestAnimationFrame(check); return; }
              sent = true;
              window.__pookiePost({id:'pookie:web-api',version:1,data:{kind:'ready'}});
            }));
          }
          requestAnimationFrame(check);
        })();
        """;
}
