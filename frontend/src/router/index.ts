import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { isManagerRole } from "@/types/auth";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: "/login",
      component: () => import("@/layouts/GuestLayout.vue"),
      meta: { guest: true },
      children: [
        {
          path: "",
          name: "login",
          component: () => import("@/views/LoginView.vue"),
        },
      ],
    },
    {
      path: "/",
      component: () => import("@/layouts/AppShell.vue"),
      meta: { requiresAuth: true },
      children: [
        {
          path: "",
          name: "home",
          redirect: () => useAuthStore().defaultHome,
        },
        {
          path: "dashboard",
          name: "dashboard",
          component: () => import("@/views/DashboardView.vue"),
          meta: { managerOnly: true },
        },
        {
          path: "jobs",
          name: "jobs",
          component: () => import("@/views/JobsListView.vue"),
        },
        {
          path: "jobs/new",
          name: "job-create",
          component: () => import("@/views/JobCreateView.vue"),
          meta: { managerOnly: true },
        },
        {
          path: "jobs/:id",
          name: "job-detail",
          component: () => import("@/views/JobDetailView.vue"),
          props: (route) => ({ id: Number(route.params.id) }),
        },
        {
          path: "jobs/:id/edit",
          name: "job-edit",
          component: () => import("@/views/JobEditView.vue"),
          meta: { managerOnly: true },
          props: (route) => ({ id: Number(route.params.id) }),
        },
        {
          path: "jobs/:id/crew",
          name: "job-crew",
          component: () => import("@/views/JobCrewView.vue"),
          meta: { managerOnly: true },
          props: (route) => ({ id: Number(route.params.id) }),
        },
        {
          path: "jobs/:id/photos",
          name: "job-photos",
          component: () => import("@/views/JobPhotosView.vue"),
          props: (route) => ({ id: Number(route.params.id) }),
        },
        {
          path: "jobs/:id/photos/new",
          name: "job-photo-upload",
          component: () => import("@/views/JobPhotoUploadView.vue"),
          props: (route) => ({ id: Number(route.params.id) }),
        },
        {
          path: "clock",
          name: "clock",
          component: () => import("@/views/ClockView.vue"),
        },
        {
          path: "account",
          name: "account",
          component: () => import("@/views/AccountView.vue"),
        },
      ],
    },
    {
      path: "/forbidden",
      name: "forbidden",
      component: () => import("@/views/ForbiddenView.vue"),
    },
    { path: "/:pathMatch(.*)*", redirect: "/" },
  ],
});

router.beforeEach(async (to) => {
  const auth = useAuthStore();
  if (!auth.initialized) {
    await auth.initialize();
  }

  if (to.meta.guest && auth.isAuthenticated) {
    return auth.defaultHome;
  }

  const requiresAuth = to.matched.some((record) => record.meta.requiresAuth);
  if (requiresAuth && !auth.isAuthenticated) {
    return { name: "login", query: { redirect: to.fullPath } };
  }

  const managerOnly = to.matched.some((record) => record.meta.managerOnly);
  if (managerOnly && !isManagerRole(auth.user?.companyRole)) {
    return { name: "forbidden" };
  }

  return true;
});

export default router;
