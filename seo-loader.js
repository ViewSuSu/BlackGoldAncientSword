(async () => {
  const MANIFEST_URL = '/.vite/manifest.json'
  const ENTRY_KEY = 'index.html'

  function appendStylesheets(cssFiles) {
    for (const href of cssFiles) {
      if (document.querySelector(`link[data-seo-css="${href}"]`)) {
        continue
      }

      const link = document.createElement('link')
      link.rel = 'stylesheet'
      link.href = `/${href}`
      link.dataset.seoCss = href
      document.head.appendChild(link)
    }
  }

  function appendModuleScript(file) {
    if (document.querySelector(`script[data-seo-entry="${file}"]`)) {
      return
    }

    const script = document.createElement('script')
    script.type = 'module'
    script.src = `/${file}`
    script.dataset.seoEntry = file
    document.body.appendChild(script)
  }

  try {
    const response = await fetch(MANIFEST_URL, { credentials: 'same-origin' })
    if (!response.ok) {
      throw new Error(`Failed to load manifest: ${response.status}`)
    }

    const manifest = await response.json()
    const entry = manifest?.[ENTRY_KEY]
    if (!entry?.file) {
      throw new Error(`Missing Vite entry: ${ENTRY_KEY}`)
    }

    appendStylesheets(Array.isArray(entry.css) ? entry.css : [])
    appendModuleScript(entry.file)
  } catch (error) {
    console.error('SEO takeover bootstrap failed', error)
  }
})()

