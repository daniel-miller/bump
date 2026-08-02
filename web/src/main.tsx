import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import "@fontsource-variable/inter";
import "@fontsource-variable/geist-mono";
// Font Awesome Pro: core plus only the styles in use. Sharp Regular is the
// app default; Sharp Solid is reserved for tiny filled glyphs (status dots).
import "@fortawesome/fontawesome-pro/css/fontawesome.min.css";
import "@fortawesome/fontawesome-pro/css/sharp-regular.min.css";
import "@fortawesome/fontawesome-pro/css/sharp-solid.min.css";
import { router } from "./router";
import { queryClient } from "./lib/queryClient";
import "./styles/globals.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>,
);
