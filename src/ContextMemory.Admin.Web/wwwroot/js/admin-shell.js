// Kortexio Admin shell — sidebar toggle (no jQuery / AdminLTE).
window.contextMemoryAdmin = window.contextMemoryAdmin || {
  toggleSidebar: function () {
    var body = document.body;
    if (!body) return;
    if (window.innerWidth <= 992) {
      body.classList.toggle("kx-sidebar-open");
      body.classList.remove("kx-sidebar-collapsed");
    } else {
      body.classList.toggle("kx-sidebar-collapsed");
      body.classList.remove("kx-sidebar-open");
    }
  },
  closeSidebarOverlay: function () {
    document.body.classList.remove("kx-sidebar-open");
  }
};
