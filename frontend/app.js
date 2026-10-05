// ShopMind V1 minimal frontend (vanilla JS)
const store = {
  get apiBase() { return localStorage.getItem('apiBase') || 'http://localhost:5000'; },
  set apiBase(v) { localStorage.setItem('apiBase', v); },
  get token() { return localStorage.getItem('token'); },
  set token(v) { v ? localStorage.setItem('token', v) : localStorage.removeItem('token'); },
};
let page = 1, pageSize = 12, totalPages = 1;

function apiUrl(p) { return store.apiBase.replace(/\/$/, '') + p; }
function headers(auth = false) {
  const h = { 'Content-Type': 'application/json' };
  if (auth && store.token) h['Authorization'] = 'Bearer ' + store.token;
  return h;
}
async function fetchJson(path, opts = {}) {
  const res = await fetch(apiUrl(path), opts);
  if (res.status === 204) return null;
  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = { raw: text }; }
  if (!res.ok) {
    const msg = data?.title || data?.message || `HTTP ${res.status}`;
    const details = data?.errors ? ' ' + JSON.stringify(data.errors) : '';
    const full = msg + details;
    // Stale token after DB reseed (backend returns 401): drop it so next
    // action forces re-login instead of repeatedly hitting FK violations.
    if (res.status === 401 && /expired|no longer exists|Unauthorized/i.test(full) && store.token) {
      store.token = null;
      if (typeof refreshAuthUI === 'function') refreshAuthUI();
    }
    throw new Error(full);
  }
  return data;
}
function notice(msg, isErr = true) {
  const el = document.getElementById('notice');
  el.textContent = msg;
  el.style.color = isErr ? '#b00' : '#0a7';
  if (msg) setTimeout(() => { if (el.textContent === msg) el.textContent = ''; }, 5000);
}
function parseJwt(t) {
  try { return JSON.parse(atob(t.split('.')[1])); } catch { return {}; }
}
function currentRole() {
  if (!store.token) return null;
  const p = parseJwt(store.token);
  return p.role || p['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || p['role'] || null;
}
function refreshAuthUI() {
  const logged = !!store.token;
  document.getElementById('authBtn').style.display = logged ? 'none' : '';
  document.getElementById('logoutBtn').style.display = logged ? '' : 'none';
  const role = currentRole();
  document.getElementById('adminBtn').style.display = role === 'Admin' ? '' : 'none';
  if (logged) loadProfile();
}
function showView(name) {
  document.querySelectorAll('.view').forEach(v => v.style.display = 'none');
  document.getElementById('view-' + name).style.display = '';
  if (name === 'shop') loadProducts(page);
  if (name === 'cart') loadCart();
  if (name === 'orders') loadOrders();
  if (name === 'admin') adminTab('products');
  window.scrollTo(0, 0);
}
function saveApiBase() {
  const v = document.getElementById('apiBase').value.trim();
  if (v) store.apiBase = v;
  notice('API base saved: ' + store.apiBase, false);
  loadProducts(1); loadCategories();
}

// --- Catalog ---
async function loadCategories() {
  try {
    const cats = await fetchJson('/api/categories');
    const sel = document.getElementById('fCategory');
    sel.innerHTML = '<option value="">All categories</option>' + cats.map(c => `<option value="${c.slug}">${c.name}</option>`).join('');
  } catch (e) { /* ignore */ }
}
async function loadProducts(p = 1) {
  page = p;
  const q = new URLSearchParams({
    page, pageSize,
    search: document.getElementById('fSearch').value,
    category: document.getElementById('fCategory').value,
    brand: document.getElementById('fBrand').value,
    sort: document.getElementById('fSort').value,
  });
  const min = document.getElementById('fMin').value, max = document.getElementById('fMax').value;
  if (min) q.set('minPrice', min);
  if (max) q.set('maxPrice', max);
  if (document.getElementById('fStock').checked) q.set('inStockOnly', 'true');
  try {
    const data = await fetchJson('/api/products?' + q.toString());
    totalPages = data.totalPages || 1;
    document.getElementById('pageInfo').textContent = `Page ${data.page}/${totalPages} · ${data.totalCount} items`;
    document.getElementById('productGrid').innerHTML = data.items.map(p => `
      <div class="card">
        <img src="${(p.images?.[0]?.url) || ''}" onerror="this.style.display='none'" />
        <h4>${p.name}</h4>
        <div>${p.brand || ''} · ${p.categoryName || ''}</div>
        <div class="price">$${p.price}</div>
        <div>Stock: ${p.availableStock}</div>
        <button onclick="viewProduct('${p.id}')">Detail</button>
        <button onclick="addToCart('${p.id}',1)">Add to Cart</button>
      </div>`).join('') || '<p>No products</p>';
  } catch (e) { notice(e.message); }
}
function prevPage() { if (page > 1) loadProducts(page - 1); }
function nextPage() { if (page < totalPages) loadProducts(page + 1); }
async function viewProduct(id) {
  try {
    const p = await fetchJson('/api/products/' + id);
    document.getElementById('productDetail').innerHTML = `
      <h3>${p.name}</h3>
      <p>${p.brand || ''} · ${p.categoryName || ''} · SKU ${p.sku}</p>
      <p>${p.description || ''}</p>
      <div class="price">$${p.price} · Stock ${p.availableStock}</div>
      <div>${(p.images || []).map(i => `<img src="${i.url}" style="max-width:200px" />`).join('')}</div>
      <div><input id="pdQty" type="number" value="1" min="1" style="width:80px" />
      <button onclick="addToCart('${p.id}', parseInt(document.getElementById('pdQty').value))">Add to Cart</button></div>`;
    showViewRaw('product');
  } catch (e) { notice(e.message); }
}
function showViewRaw(name) {
  document.querySelectorAll('.view').forEach(v => v.style.display = 'none');
  document.getElementById('view-' + name).style.display = '';
}

// --- Auth ---
async function doLogin() {
  try {
    const data = await fetchJson('/api/auth/login', { method: 'POST', headers: headers(), body: JSON.stringify({ email: document.getElementById('lEmail').value, password: document.getElementById('lPass').value }) });
    store.token = data.token;
    notice('Logged in', false); refreshAuthUI(); updateCartBadge(); showView('shop');
  } catch (e) { notice(e.message); }
}
async function doRegister() {
  try {
    const data = await fetchJson('/api/auth/register', { method: 'POST', headers: headers(), body: JSON.stringify({ email: document.getElementById('rEmail').value, password: document.getElementById('rPass').value, firstName: document.getElementById('rFirst').value || 'New', lastName: document.getElementById('rLast').value || 'User', phoneNumber: null }) });
    store.token = data.token;
    notice('Registered', false); refreshAuthUI(); showView('shop');
  } catch (e) { notice(e.message); }
}
function logout() { store.token = null; refreshAuthUI(); showView('shop'); }
async function loadProfile() {
  if (!store.token) return;
  try {
    const me = await fetchJson('/api/auth/me', { headers: headers(true) });
    document.getElementById('profile').innerHTML = `<p>Logged in as <b>${me.email}</b> (${me.role})</p>`;
  } catch { /* ignore */ }
}

// --- Cart ---
async function updateCartBadge() {
  if (!store.token) { document.getElementById('cartCount').textContent = '0'; return; }
  try {
    const c = await fetchJson('/api/cart', { headers: headers(true) });
    document.getElementById('cartCount').textContent = c.totalQuantity ?? 0;
  } catch { }
}
async function addToCart(productId, qty) {
  if (!store.token) { notice('Please login first'); showView('auth'); return; }
  try {
    await fetchJson('/api/cart/items', { method: 'POST', headers: headers(true), body: JSON.stringify({ productId, quantity: qty || 1 }) });
    notice('Added to cart', false); updateCartBadge();
  } catch (e) { notice(e.message); }
}
async function loadCart() {
  if (!store.token) { document.getElementById('cartDetail').innerHTML = '<p>Please login</p>'; return; }
  try {
    const c = await fetchJson('/api/cart', { headers: headers(true) });
    document.getElementById('cartCount').textContent = c.totalQuantity;
    document.getElementById('cartDetail').innerHTML = (c.items || []).map(i => `
      <div>${i.productName} (${i.sku}) — $${i.unitPrice} × <input type="number" value="${i.quantity}" min="1" style="width:70px" onchange="updateQty('${i.id}', this.value)" /> = $${i.subtotal}
      <button onclick="removeItem('${i.id}')">Remove</button></div>`).join('') + `<h3>Subtotal $${c.subtotal}</h3><button onclick="clearCart()">Clear cart</button>`;
  } catch (e) { notice(e.message); }
}
async function updateQty(id, qty) {
  try { await fetchJson('/api/cart/items/' + id, { method: 'PATCH', headers: headers(true), body: JSON.stringify({ quantity: parseInt(qty) }) }); loadCart(); } catch (e) { notice(e.message); }
}
async function removeItem(id) {
  try { await fetchJson('/api/cart/items/' + id, { method: 'DELETE', headers: headers(true) }); loadCart(); updateCartBadge(); } catch (e) { notice(e.message); }
}
async function clearCart() {
  try { await fetchJson('/api/cart', { method: 'DELETE', headers: headers(true) }); loadCart(); updateCartBadge(); } catch (e) { notice(e.message); }
}

// --- Checkout & Orders ---
async function doCheckout() {
  try {
    const data = await fetchJson('/api/checkout', { method: 'POST', headers: headers(true), body: JSON.stringify({
      shippingAddress: { fullName: document.getElementById('cName').value, phone: document.getElementById('cPhone').value, address: document.getElementById('cAddr').value, city: document.getElementById('cCity').value },
      paymentMethod: 'fake', simulateFailure: document.getElementById('cFail').checked, simulateTimeout: document.getElementById('cTimeout').checked }) });
    document.getElementById('checkoutResult').innerHTML = `<p style="color:green">Order ${data.order.orderNumber} created · Total $${data.order.totalAmount} · Payment ${data.payment.status}</p>`;
    updateCartBadge(); notice('Checkout success', false);
  } catch (e) { notice(e.message); document.getElementById('checkoutResult').innerHTML = `<p style="color:red">${e.message}</p>`; }
}
async function loadOrders() {
  if (!store.token) { document.getElementById('orderList').innerHTML = '<p>Please login</p>'; return; }
  try {
    const data = await fetchJson('/api/orders?page=1&pageSize=20', { headers: headers(true) });
    document.getElementById('orderDetail').innerHTML = '';
    document.getElementById('orderList').innerHTML = `<table><tr><th>Number</th><th>Status</th><th>Total</th><th>Date</th><th></th></tr>` +
      data.items.map(o => `<tr><td>${o.orderNumber}</td><td>${o.status}</td><td>$${o.totalAmount}</td><td>${new Date(o.createdAt).toLocaleString()}</td><td><button onclick="viewOrder('${o.id}')">View</button> <button onclick="cancelOrder('${o.id}')">Cancel</button></td></tr>`).join('') + `</table>`;
  } catch (e) { notice(e.message); }
}
async function viewOrder(id) {
  try {
    const o = await fetchJson('/api/orders/' + id, { headers: headers(true) });
    document.getElementById('orderDetail').innerHTML = `<h3>${o.orderNumber} — ${o.status}</h3><p>${o.shippingAddress.fullName}, ${o.shippingAddress.address}, ${o.shippingAddress.city}</p>` +
      o.items.map(i => `<div>${i.productName} × ${i.quantity} = $${i.subtotal}</div>`).join('') + `<p>Subtotal $${o.subtotal} + Ship $${o.shippingFee} = <b>$${o.totalAmount}</b></p>`;
  } catch (e) { notice(e.message); }
}
async function cancelOrder(id) {
  try { await fetchJson(`/api/orders/${id}/cancel`, { method: 'POST', headers: headers(true) }); notice('Cancelled', false); loadOrders(); } catch (e) { notice(e.message); }
}

// --- Admin ---
function adminTab(tab) {
  if (currentRole() !== 'Admin') { document.getElementById('adminContent').innerHTML = '<p>Admin only. Login as admin@shopmind.local / Admin123!</p>'; return; }
  if (tab === 'products') adminProducts();
  if (tab === 'categories') adminCategories();
  if (tab === 'inventory') adminInventory();
  if (tab === 'orders') adminOrders();
}
async function adminProducts() {
  const el = document.getElementById('adminContent');
  try {
    const data = await fetchJson('/api/admin/products?page=1&pageSize=20', { headers: headers(true) });
    el.innerHTML = `<h3>Products (${data.totalCount})</h3><table><tr><th>Name</th><th>SKU</th><th>Price</th><th>Status</th><th>Stock</th><th></th></tr>` +
      data.items.map(p => `<tr><td>${p.name}</td><td>${p.sku}</td><td>$${p.price}</td><td>${p.status}</td><td>${p.availableStock}</td><td><button onclick="adminDeleteProduct('${p.id}')">Deactivate</button></td></tr>`).join('') + `</table>
      <h4>Create product</h4><div class="form"><input id="apName" placeholder="Name" /><input id="apSku" placeholder="SKU" /><input id="apPrice" type="number" placeholder="Price" /><input id="apBrand" placeholder="Brand" /><input id="apCat" placeholder="CategoryId" /><input id="apStock" type="number" placeholder="Stock" value="10" /><button onclick="adminCreateProduct()">Create (Active)</button></div><p class="hint">Tip: copy a CategoryId from Categories tab.</p>`;
  } catch (e) { notice(e.message); }
}
async function adminCreateProduct() {
  try {
    const catId = document.getElementById('apCat').value;
    await fetchJson('/api/admin/products', { method: 'POST', headers: headers(true), body: JSON.stringify({ name: document.getElementById('apName').value, slug: null, sku: document.getElementById('apSku').value, categoryId: catId, description: '', brand: document.getElementById('apBrand').value || null, price: parseFloat(document.getElementById('apPrice').value), status: 1, images: [], initialStock: parseInt(document.getElementById('apStock').value) || 0 }) });
    notice('Created', false); adminProducts();
  } catch (e) { notice(e.message); }
}
async function adminDeleteProduct(id) {
  try { await fetchJson('/api/admin/products/' + id, { method: 'DELETE', headers: headers(true) }); adminProducts(); } catch (e) { notice(e.message); }
}
async function adminCategories() {
  const el = document.getElementById('adminContent');
  try {
    const cats = await fetchJson('/api/admin/categories', { headers: headers(true) });
    el.innerHTML = `<h3>Categories</h3><table><tr><th>Name</th><th>Slug</th><th>Id</th><th>Active</th></tr>` + cats.map(c => `<tr><td>${c.name}</td><td>${c.slug}</td><td><small>${c.id}</small></td><td>${c.isActive}</td></tr>`).join('') + `</table>
      <h4>Create category</h4><div class="form"><input id="acName" placeholder="Name" /><button onclick="adminCreateCategory()">Create</button></div>`;
  } catch (e) { notice(e.message); }
}
async function adminCreateCategory() {
  try { await fetchJson('/api/admin/categories', { method: 'POST', headers: headers(true), body: JSON.stringify({ name: document.getElementById('acName').value, slug: null, description: null, isActive: true }) }); adminCategories(); } catch (e) { notice(e.message); }
}
async function adminInventory() {
  const el = document.getElementById('adminContent');
  try {
    const data = await fetchJson('/api/admin/inventory?page=1&pageSize=20', { headers: headers(true) });
    el.innerHTML = `<h3>Inventory</h3><table><tr><th>Product</th><th>SKU</th><th>Qty</th><th>Avail</th><th>Set Qty</th></tr>` +
      data.items.map(i => `<tr><td>${i.productName}</td><td>${i.sku}</td><td>${i.quantity}</td><td>${i.availableQuantity}</td><td><input id="inv-${i.productId}" type="number" value="${i.quantity}" style="width:80px" /><button onclick="adminSetInv('${i.productId}')">Save</button></td></tr>`).join('') + `</table>`;
  } catch (e) { notice(e.message); }
}
async function adminSetInv(pid) {
  try { await fetchJson('/api/admin/inventory/' + pid, { method: 'PATCH', headers: headers(true), body: JSON.stringify({ quantity: parseInt(document.getElementById('inv-' + pid).value) }) }); notice('Saved', false); adminInventory(); } catch (e) { notice(e.message); }
}
async function adminOrders() {
  const el = document.getElementById('adminContent');
  try {
    const data = await fetchJson('/api/admin/orders?page=1&pageSize=20', { headers: headers(true) });
    el.innerHTML = `<h3>Orders</h3><table><tr><th>Number</th><th>Status</th><th>Total</th><th>Set status</th></tr>` +
      data.items.map(o => `<tr><td>${o.orderNumber}</td><td>${o.status}</td><td>$${o.totalAmount}</td><td><select id="st-${o.id}"><option>Paid</option><option>Processing</option><option>Shipped</option><option>Delivered</option><option>Cancelled</option></select><button onclick="adminSetStatus('${o.id}')">Update</button></td></tr>`).join('') + `</table>`;
  } catch (e) { notice(e.message); }
}
async function adminSetStatus(id) {
  try { await fetchJson(`/api/admin/orders/${id}/status`, { method: 'PATCH', headers: headers(true), body: JSON.stringify({ status: document.getElementById('st-' + id).value }) }); notice('Updated', false); adminOrders(); } catch (e) { notice(e.message); }
}

document.getElementById('apiBase').value = store.apiBase;
refreshAuthUI(); loadCategories(); loadProducts(1); updateCartBadge();
