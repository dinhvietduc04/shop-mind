import type { Metadata } from "next";
import { Archivo, JetBrains_Mono } from "next/font/google";
import { Providers } from "@/components/shell";
import "./globals.css";

const archivo = Archivo({
  subsets: ["latin"],
  variable: "--font-archivo",
  weight: ["400", "500", "600", "700", "800"],
});

const jetmono = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-jetmono",
  weight: ["400", "700"],
});

export const metadata: Metadata = {
  title: {
    default: "ShopMind bin catalog",
    template: "%s · ShopMind",
  },
  description:
    "Electronics store demo where an AI assistant shops through MCP tools. At the Assist level, the assistant quotes and a human pays.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning className={`${archivo.variable} ${jetmono.variable}`}>
      <body className="min-h-screen antialiased">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
