import type { CategoryDto, ProductDto, Specs, UserDto } from "@/lib/api/types";

/**
 * Fixtures transcribed from backend/Shop.Infrastructure/Seed/SeedData.cs
 * (names, SKUs, brands, prices, stock exact). GUIDs are deterministic mock
 * ids — the real API returns its own. `specs` is the A3 forward contract,
 * authored here at full fidelity and labeled synthetic.
 * Local photos: public/products/ (see PROVENANCE.md); products without one
 * render the honest empty plate.
 */

export const MOCK_USERS: Record<string, { password: string; user: UserDto }> = {
  "admin@shopmind.local": {
    password: "Admin123!",
    user: { id: "u-11111111-1111-1111-1111-111111111111", email: "admin@shopmind.local", firstName: "Shop", lastName: "Admin", phoneNumber: null, role: "Admin" },
  },
  "customer@shopmind.local": {
    password: "Customer123!",
    user: { id: "u-22222222-2222-2222-2222-222222222222", email: "customer@shopmind.local", firstName: "Demo", lastName: "Customer", phoneNumber: null, role: "Customer" },
  },
};

export { FREE_SHIPPING_THRESHOLD, FLAT_SHIPPING_FEE } from "@/lib/shipping";

interface Seed {
  slug: string; name: string; sku: string; brand: string;
  price: number; stock: number; category: string; description: string;
  specs: Specs; photo?: string;
}

const SEED: Seed[] = [
  { slug: "thinkpad-x1-carbon-gen-11", name: "ThinkPad X1 Carbon Gen 11", sku: "LEN-X1C-G11", brand: "Lenovo", price: 1499.0, stock: 25, category: "laptops", description: '14" business ultrabook, Intel i7, 16GB RAM, 512GB SSD.', specs: { ram_gb: 16, storage_gb: 512, screen_in: 14, cpu: "Intel Core i7", weight_kg: 1.12, thunderbolt: true }, photo: "/products/thinkpad-x1c.jpg" },
  { slug: "macbook-air-m3", name: "MacBook Air M3", sku: "APL-MBA-M3", brand: "Apple", price: 1099.0, stock: 20, category: "laptops", description: '13" MacBook Air with M3 chip, 8GB RAM, 256GB SSD.', specs: { ram_gb: 8, storage_gb: 256, screen_in: 13.6, cpu: "Apple M3", weight_kg: 1.24 } },
  { slug: "galaxy-s24-ultra", name: "Galaxy S24 Ultra", sku: "SAM-S24U", brand: "Samsung", price: 1199.99, stock: 40, category: "smartphones", description: "Flagship smartphone with 200MP camera.", specs: { ram_gb: 12, storage_gb: 256, screen_in: 6.8, camera_mp: 200, s_pen: true } },
  { slug: "iphone-15-pro", name: "iPhone 15 Pro", sku: "APL-IP15P", brand: "Apple", price: 999.0, stock: 35, category: "smartphones", description: '6.1" iPhone 15 Pro, A17 Pro.', specs: { ram_gb: 8, storage_gb: 128, screen_in: 6.1, cpu: "A17 Pro" } },
  { slug: "ultrasharp-27-4k", name: "UltraSharp 27 4K Monitor", sku: "DEL-U2723QE", brand: "Dell", price: 549.99, stock: 15, category: "monitors", description: '27" 4K USB-C monitor.', specs: { screen_in: 27, resolution: "3840x2160", usb_c: true, panel: "IPS" } },
  { slug: "mx-keys-s", name: "MX Keys S Keyboard", sku: "LOG-MXKEYS-S", brand: "Logitech", price: 109.99, stock: 60, category: "keyboards", description: "Wireless illuminated keyboard.", specs: { wireless: true, backlit: true, layout: "full" } },
  { slug: "mx-master-3s", name: "MX Master 3S Mouse", sku: "LOG-MXM3S", brand: "Logitech", price: 99.99, stock: 80, category: "mice", description: "Wireless performance mouse.", specs: { wireless: true, dpi_max: 8000, buttons: 7 } },
  { slug: "wh-1000xm5", name: "WH-1000XM5 Headphones", sku: "SNY-WH1000XM5", brand: "Sony", price: 349.99, stock: 30, category: "headphones", description: "Noise cancelling headphones.", specs: { anc: true, battery_h: 30, wireless: true }, photo: "/products/sony-wh1000.jpg" },
  { slug: "rtx-4070", name: "RTX 4070 GPU", sku: "NV-RTX4070", brand: "NVIDIA", price: 599.99, stock: 12, category: "components", description: "12GB GDDR6X graphics card.", specs: { vram_gb: 12, interface: "PCIe 4.0", tdp_w: 200 } },
  { slug: "usb-c-dock", name: "USB-C Docking Station", sku: "ACC-USBCDOCK", brand: "Anker", price: 49.99, stock: 100, category: "accessories", description: "7-in-1 USB-C hub.", specs: { ports: 7, "4k_hdmi": true, pd_w: 100 } },
];

const CATS: { slug: string; name: string; description: string }[] = [
  { slug: "laptops", name: "Laptops", description: "Laptops and notebooks" },
  { slug: "smartphones", name: "Smartphones", description: "Smartphones and mobiles" },
  { slug: "monitors", name: "Monitors", description: "Monitors and displays" },
  { slug: "keyboards", name: "Keyboards", description: "Keyboards" },
  { slug: "mice", name: "Mice", description: "Mice and pointing devices" },
  { slug: "headphones", name: "Headphones", description: "Headphones and audio" },
  { slug: "components", name: "Components", description: "PC components" },
  { slug: "accessories", name: "Accessories", description: "Accessories" },
];

const catId = (slug: string) => `c-${slug}`;

export const FIXTURE_CATEGORIES: CategoryDto[] = CATS.map((c) => ({
  id: catId(c.slug), name: c.name, slug: c.slug, description: c.description, isActive: true,
}));

export interface FixtureProduct extends ProductDto {
  description: string;
  quantity: number; // total stock; reserved tracked in db
  createdAt: string;
}

export const FIXTURE_PRODUCTS: FixtureProduct[] = SEED.map((s, i) => {
  const category = FIXTURE_CATEGORIES.find((c) => c.slug === s.category)!;
  return {
    id: `p-${s.slug}`,
    name: s.name,
    slug: s.slug,
    sku: s.sku,
    brand: s.brand,
    price: s.price,
    status: "Active",
    categoryId: category.id,
    categoryName: category.name,
    availableStock: s.stock, // recomputed live by handlers
    images: [{ id: `img-${s.slug}`, url: s.photo ?? "", altText: s.name, displayOrder: 0 }],
    specs: s.specs,
    description: s.description,
    quantity: s.stock,
    createdAt: new Date(Date.UTC(2026, 9, 1 + i)).toISOString(),
  };
});

export const PRODUCT_CATEGORIES = Object.fromEntries(
  SEED.map((s, i) => [FIXTURE_PRODUCTS[i].id, s.category])
) as Record<string, string>;
