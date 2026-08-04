import { createBrowserRouter } from "react-router-dom";
import { BoardPage } from "@/routes/public/BoardPage";
import { HostGate } from "@/routes/public/HostGate";
import { LoginPage } from "@/routes/auth/LoginPage";
import { TwoFactorPage } from "@/routes/auth/TwoFactorPage";
import { SubscribeConfirmPage } from "@/routes/public/SubscribeConfirmPage";
import { UnsubscribePage } from "@/routes/public/UnsubscribePage";
import { ConfirmEmailChangePage } from "@/routes/public/ConfirmEmailChangePage";
import { AppShell } from "@/components/AppShell";
import { DashboardPage } from "@/routes/admin/DashboardPage";
import { ServicesListPage } from "@/routes/admin/ServicesListPage";
import { ServiceDetailPage } from "@/routes/admin/ServiceDetailPage";
import { OutagesListPage } from "@/routes/admin/OutagesListPage";
import { OutageDetailPage } from "@/routes/admin/OutageDetailPage";
import { OwnersListPage } from "@/routes/admin/OwnersListPage";
import { OwnerDetailPage } from "@/routes/admin/OwnerDetailPage";
import { AnnouncementsPage } from "@/routes/admin/AnnouncementsPage";
import { AppsPage } from "@/routes/admin/AppsPage";
import { EnvironmentsPage } from "@/routes/admin/EnvironmentsPage";
import { ServersPage } from "@/routes/admin/ServersPage";
import { ProblemsPage } from "@/routes/admin/ProblemsPage";
import { ProblemDetailPage } from "@/routes/admin/ProblemDetailPage";
import { AccountDetailsPage } from "@/routes/admin/AccountDetailsPage";
import { AccountSecurityPage } from "@/routes/admin/AccountSecurityPage";
import { AboutPage } from "@/routes/admin/AboutPage";

// Admin pages sit at the root with no /admin prefix: bump is an admin-only
// app, so the segment carried no information. Public routes coexist beside
// them; /boards/:handle is the public status board (the admin owner detail
// lives at /owners/:handle).
export const router = createBrowserRouter([
  { path: "/", element: <HostGate /> },
  { path: "/login", element: <LoginPage /> },
  { path: "/login/mfa", element: <TwoFactorPage /> },
  { path: "/boards/:handle", element: <BoardPage /> },
  { path: "/subscribe/confirm", element: <SubscribeConfirmPage /> },
  { path: "/unsubscribe", element: <UnsubscribePage /> },
  { path: "/account/confirm-email", element: <ConfirmEmailChangePage /> },
  {
    element: <AppShell />,
    children: [
      { path: "/dashboard", element: <DashboardPage /> },
      { path: "/services", element: <ServicesListPage /> },
      { path: "/services/:handle", element: <ServiceDetailPage /> },
      { path: "/outages", element: <OutagesListPage /> },
      { path: "/outages/:id", element: <OutageDetailPage /> },
      { path: "/owners", element: <OwnersListPage /> },
      { path: "/owners/:handle", element: <OwnerDetailPage /> },
      { path: "/announcements", element: <AnnouncementsPage /> },
      { path: "/apps", element: <AppsPage /> },
      { path: "/environments", element: <EnvironmentsPage /> },
      { path: "/servers", element: <ServersPage /> },
      { path: "/problems", element: <ProblemsPage /> },
      { path: "/problems/:id", element: <ProblemDetailPage /> },
      { path: "/account", element: <AccountDetailsPage /> },
      { path: "/security", element: <AccountSecurityPage /> },
      { path: "/about", element: <AboutPage /> },
    ],
  },
]);
