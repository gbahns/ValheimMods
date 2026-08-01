/* Valheim Knowledgebase — shared top-nav injector.
   Include with: <script src="shared/chrome.js" defer></script>
   Pair with:    <link rel="stylesheet" href="shared/styles.css">

   Adding a new page? Add an entry to PAGES below and you're done — every
   guide picks up the new link on next reload. The data-page attribute is
   used by the active-highlight logic, so it must match the page filename
   (sans .html) of the corresponding page. */

(function () {
    const PAGES = [
        { page: 'index',     label: 'Home' },
        { page: 'ships',     label: 'Ships' },
        { page: 'weapons',   label: 'Weapons' },
        { page: 'damage',    label: 'Damage' },
        { page: 'fishing',   label: 'Fishing' },
        { page: 'food',      label: 'Food' },
        { page: 'bog-witch', label: 'Bog Witch' },
    ];

    const links = PAGES.map(p =>
        `<a href="${p.page}.html" data-page="${p.page}">${p.label}</a>`
    ).join('');

    const navHtml = `
        <header class="kb-nav">
            <div class="kb-nav-inner">
                <a href="index.html" class="kb-brand">📜 Valheim Knowledgebase</a>
                <nav class="kb-links">${links}</nav>
            </div>
        </header>
    `;

    const inject = () => {
        document.body.insertAdjacentHTML('afterbegin', navHtml);
        // Highlight current page: filename without extension, falling back to 'index'.
        const path = (location.pathname.split('/').pop() || 'index.html').replace('.html', '') || 'index';
        const current = document.querySelector(`.kb-links a[data-page="${path}"]`);
        if (current) current.classList.add('kb-active');
    };

    if (document.body) inject();
    else document.addEventListener('DOMContentLoaded', inject);
})();
