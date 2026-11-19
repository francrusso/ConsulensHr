$(function () {
  "use strict";
  $(function () {
    $(".metismenu-card").metisMenu({
      toggle: false,
      triggerElement: ".card-header",
      parentTrigger: ".card",
      subMenu: ".card-body",
    });
  });
  // Tooltips
  $(function () {
    $('[data-toggle="tooltip"]').tooltip();
  });
  // Metishmenu card collapse
  $(function () {
    $(".card-collapse").metisMenu({
      toggle: false,
      triggerElement: ".card-header",
      parentTrigger: ".card",
      subMenu: ".card-body",
    });
  });

  // Sidebar: desktop collapse and mobile drawer use the same wrapper state.
  const mobileSidebar = window.matchMedia("(max-width: 1024px)");
  const sidebar = document.querySelector(".sidebar-wrapper");
  const mobileTrigger = document.querySelector(".toggle-btn-mobile");
  let isSidebarToggled = $(".wrapper").hasClass("toggled");
  let desktopSidebarToggled = mobileSidebar.matches ? false : isSidebarToggled;

  // Existing layouts may still be served by a running compiled Razor process.
  // Create the backdrop here so it works without depending on new markup.
  if (sidebar) {
    sidebar.id = sidebar.id || "main-sidebar";
    if (!document.querySelector(".wrapper > .overlay")) {
      const backdrop = document.createElement("div");
      backdrop.className = "overlay";
      backdrop.setAttribute("aria-hidden", "true");
      document.querySelector(".wrapper").append(backdrop);
    }
    $(".toggle-btn-mobile").attr({
      "aria-label": "Apri il menù", "aria-controls": sidebar.id, role: "button"
    });
    $(".close-menu-mobile").attr({ "aria-label": "Chiudi il menù", role: "button" });
  }

  function setSidebarToggled(toggled) {
    const mobile = mobileSidebar.matches;
    $(".wrapper").toggleClass("toggled", toggled);
    $("body").toggleClass("mobile-menu-open", mobile && toggled);
    $(".sidebar-collapse").attr("aria-expanded", String(!toggled));
    $(".toggle-btn-mobile").attr("aria-expanded", String(mobile && toggled));
    $(".close-menu-mobile").toggle(mobile && toggled);
    $("#logo-img").toggle(mobile || !toggled);
    $("#logo-icon").toggle(!mobile && toggled);
    if (sidebar) sidebar.inert = mobile && !toggled;
    isSidebarToggled = toggled;
    if (!mobile) desktopSidebarToggled = toggled;
  }

  function closeMobileSidebar(restoreFocus) {
    if (!mobileSidebar.matches || !isSidebarToggled) return;
    setSidebarToggled(false);
    if (restoreFocus && mobileTrigger) mobileTrigger.focus();
  }

  $(".toggle-btn").on("click", function () { setSidebarToggled(!isSidebarToggled); });
  $(".toggle-btn-mobile").on("click", function () {
    setSidebarToggled(!isSidebarToggled);
    if (isSidebarToggled) document.querySelector(".close-menu-mobile")?.focus();
  });
  $(".close-menu-mobile, .wrapper > .overlay").on("click", function () { closeMobileSidebar(true); });
  $(".toggle-btn-mobile, .close-menu-mobile").on("keydown", function (event) {
    if (this.tagName === "A" && event.key === " ") { event.preventDefault(); this.click(); }
  });
  $(document).on("keydown", function (event) {
    if (!mobileSidebar.matches || !isSidebarToggled || !sidebar) return;
    if (event.key === "Escape") { closeMobileSidebar(true); return; }
    if (event.key !== "Tab") return;
    const focusable = Array.from(sidebar.querySelectorAll('a[href], button, input, select, [tabindex="0"]'))
      .filter(el => !el.disabled && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden");
    const first = focusable[0], last = focusable[focusable.length - 1];
    if (!first) return;
    if (event.shiftKey && (document.activeElement === first || !sidebar.contains(document.activeElement))) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && (document.activeElement === last || !sidebar.contains(document.activeElement))) {
      event.preventDefault(); first.focus();
    }
  });
  mobileSidebar.addEventListener("change", function () {
    setSidebarToggled(mobileSidebar.matches ? false : desktopSidebarToggled);
  });
  setSidebarToggled(mobileSidebar.matches ? false : isSidebarToggled);

  // === sidebar menu activation js
  $(function () {
  var currentPath = window.location.pathname.toLowerCase().replace(/\/$/, "");

  var $currentLink = $(".metismenu li a").filter(function () {
    var href = $(this).attr("href");
    if (!href || href === "#" || href === "javascript:;") return false;

    try {
      var url = new URL(href, window.location.origin);

      if (url.origin !== window.location.origin) {
        return false;
      }

      var linkPath = url.pathname.toLowerCase().replace(/\/$/, "");
      return linkPath === currentPath;
    } catch (e) {
      return false;
    }
  }).first();

  if ($currentLink.length) {
    $currentLink.addClass("active");
    $currentLink.attr("aria-current", "page");

    var $parentLi = $currentLink.parent("li");
    $parentLi.addClass("mm-active");
    $parentLi.parents("ul").addClass("mm-show");
    $parentLi.parents("ul").parent("li").addClass("mm-active");
  }
});
    // metismenu
    $(function () {
      $("#menu").metisMenu();
    });
  /* Back To Top */
  $(document).ready(function () {
    $(window).on("scroll", function () {
      const pastHeader = $(this).scrollTop() > 300;
      $(".top-header").toggleClass("sticky-top-header", pastHeader);
      if (pastHeader) {
        $(".back-to-top").fadeIn();
      } else {
        $(".back-to-top").fadeOut();
      }
    });
    $(".back-to-top").on("click", function () {
      $("html, body").animate(
        {
          scrollTop: 0,
        },
        600
      );
      return false;
    });
  });


});

// Search only navigation entries rendered for the current user's permissions.
$(function () {
    const input = document.getElementById("navigation-search");
    const results = document.getElementById("navigation-search-results");
    const status = document.getElementById("navigation-search-status");
    if (!input || !results) return;
    const normalize = text => text.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("it");
    const seen = new Set();
    const entries = Array.from(document.querySelectorAll(".sidebar-wrapper a[href]")).flatMap(link => {
        const href = link.getAttribute("href");
        if (!href || href.startsWith("javascript:") || href === "#" || /logout/i.test(href) || link.classList.contains("sidebar-logout")) return [];
        const url = new URL(href, window.location.origin);
        if (url.origin !== window.location.origin || seen.has(url.href)) return [];
        seen.add(url.href);
        const label = link.textContent.trim().replace(/\s+/g, " ");
        const group = link.closest("ul")?.parentElement.querySelector(":scope > a .menu-title")?.textContent.trim() || "";
        return [{ label, group, href: url.href, search: normalize(label + " " + group) }];
    });
    const subscriptionUrl = input.dataset.subscriptionSearchUrl;
    if (subscriptionUrl) {
        entries.push({
            label: "Abbonamento",
            group: "Profilo Consulens",
            href: new URL(subscriptionUrl, window.location.origin).href,
            search: normalize("abbonamento piano rinnovo sottoscrizione")
        });
        const cancellationUrl = new URL(subscriptionUrl, window.location.origin);
        cancellationUrl.hash = "disdetta-abbonamento";
        entries.push({
            label: "Disdetta abbonamento",
            group: "Profilo Consulens",
            href: cancellationUrl.href,
            search: normalize("disdetta abbonamento annullamento rinnovo recesso")
        });
    }
    function close() { results.hidden = true; input.setAttribute("aria-expanded", "false"); }
    function search() {
        const query = normalize(input.value.trim());
        results.replaceChildren();
        if (!query) { close(); if (status) status.textContent = ""; return; }
        const words = query.split(/\s+/).filter(Boolean);
        const matches = entries.filter(entry => words.every(word => entry.search.includes(word)))
            .sort((a, b) => Number(normalize(b.label).startsWith(query)) - Number(normalize(a.label).startsWith(query)))
            .slice(0, 8);
        matches.forEach(entry => {
            const link = document.createElement("a");
            link.href = entry.href;
            link.textContent = entry.label;
            if (entry.group) {
                const group = document.createElement("small");
                group.textContent = entry.group;
                link.append(group);
            }
            results.append(link);
        });
        if (!matches.length) {
            const empty = document.createElement("p");
            empty.textContent = "Nessuna sezione trovata";
            results.append(empty);
        }
        if (status) status.textContent = matches.length + " sezioni trovate";
        results.hidden = false;
        input.setAttribute("aria-expanded", "true");
    }
    input.addEventListener("input", search);
    input.addEventListener("focus", search);
    input.addEventListener("keydown", event => {
        if (event.key === "ArrowDown") { event.preventDefault(); search(); results.querySelector("a")?.focus(); }
        if (event.key === "Enter" && !results.hidden) { event.preventDefault(); results.querySelector("a")?.click(); }
        if (event.key === "Escape") close();
    });
    results.addEventListener("keydown", event => {
        const links = Array.from(results.querySelectorAll("a"));
        const index = links.indexOf(document.activeElement);
        if (event.key === "Escape") { input.focus(); close(); }
        if (event.key === "ArrowDown") { event.preventDefault(); links[(index + 1) % links.length]?.focus(); }
        if (event.key === "ArrowUp") { event.preventDefault(); if (index <= 0) input.focus(); else links[index - 1]?.focus(); }
    });
    document.addEventListener("click", event => { if (!input.closest(".topbar-search").contains(event.target)) close(); });
    input.closest(".topbar-search").addEventListener("focusout", event => {
        if (!event.currentTarget.contains(event.relatedTarget)) close();
    });
});
