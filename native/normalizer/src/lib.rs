//! A C ABI around normalizer-tr 0.8 (Apache-2.0) for the Quick Prompt engine: written Turkish in, the
//! words a voice should say out ("12 Kasım 14.30'a" -> "on iki Kasım on dört otuza"). The engine loads
//! this library and calls `qp_normalize_tr`; the result must go back through `qp_free`.
//! It never fails: text it cannot read comes back as it was.

use std::ffi::{CStr, CString, c_char};
use std::sync::OnceLock;

use normalizer_tr::{AmbiguityPolicy, NormalizeOptions, Normalizer};

/// The normalizer reads at most 32 KiB at once; blocks are cut at spaces well below that.
const BLOCK_BYTES: usize = 8 * 1024;

fn normalizer() -> Option<&'static Normalizer> {
    static CELL: OnceLock<Option<Normalizer>> = OnceLock::new();
    CELL.get_or_init(|| Normalizer::new().ok()).as_ref()
}

fn spoken(block: &str) -> String {
    let options = NormalizeOptions { ambiguity_policy: AmbiguityPolicy::Fallback, ..Default::default() };
    match normalizer().map(|n| n.normalize(block, &options)) {
        Some(Ok(result)) => result.normalized_text().to_string(),
        _ => block.to_string(),
    }
}

/// Normalize `text` the way EMA Lightning's frontend does ("fallback": nothing is left unread).
pub fn normalize(text: &str) -> String {
    let mut out: Vec<String> = Vec::new();
    let mut block = String::new();
    for word in text.split_whitespace() {
        if !block.is_empty() && block.len() + word.len() + 1 > BLOCK_BYTES {
            out.push(spoken(&block));
            block.clear();
        }
        if !block.is_empty() {
            block.push(' ');
        }
        block.push_str(word);
    }
    if !block.is_empty() {
        out.push(spoken(&block));
    }
    out.join(" ")
}

/// # Safety
/// `input` must be a valid NUL-terminated UTF-8 string (invalid bytes are replaced).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn qp_normalize_tr(input: *const c_char) -> *mut c_char {
    if input.is_null() {
        return std::ptr::null_mut();
    }
    let text = unsafe { CStr::from_ptr(input) }.to_string_lossy();
    let result = std::panic::catch_unwind(|| normalize(&text)).unwrap_or_else(|_| text.to_string());
    CString::new(result.replace('\0', " ")).map(CString::into_raw).unwrap_or(std::ptr::null_mut())
}

/// # Safety
/// `text` must come from `qp_normalize_tr` and be freed once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn qp_free(text: *mut c_char) {
    if !text.is_null() {
        drop(unsafe { CString::from_raw(text) });
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn reads_numbers_and_times() {
        let out = super::normalize("Toplantıyı 12 Kasım saat 14.30'a aldım.");
        assert!(out.contains("on iki"), "{out}");
    }
}
