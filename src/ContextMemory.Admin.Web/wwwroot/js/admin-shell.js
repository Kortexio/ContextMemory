// Lightweight AdminLTE pushmenu substitute (no jQuery).
window.contextMemoryAdmin = window.contextMemoryAdmin || {
  toggleSidebar: function () {
    var body = document.body;
    if (!body) return;
    if (window.innerWidth <= 992) {
      body.classList.toggle('sidebar-open');
      body.classList.remove('sidebar-collapse');
    } else {
      body.classList.toggle('sidebar-collapse');
      body.classList.remove('sidebar-open');
    }
  },
  closeSidebarOverlay: function () {
    document.body.classList.remove('sidebar-open');
  }
};
