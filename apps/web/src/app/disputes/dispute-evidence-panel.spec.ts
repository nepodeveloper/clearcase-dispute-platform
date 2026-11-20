import { HttpEventType } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CustomerEvidence } from './dispute-api';
import { DisputeEvidencePanel, evidenceFileProblem, formatFileSize } from './dispute-evidence-panel';

describe('DisputeEvidencePanel', () => {
  const disputeId = 'aaaaaaaa-1111-4222-8333-444455556666';
  const listUrl = `/api/v1/disputes/${disputeId}/evidence`;
  const stored: CustomerEvidence = {
    id: 'ev-1', fileName: 'receipt.pdf', contentType: 'application/pdf', sizeBytes: 2048,
    uploadedAt: '2026-11-12T10:00:00Z', note: 'Shop receipt',
  };
  let http: HttpTestingController;
  let fixture: ComponentFixture<DisputeEvidencePanel>;
  let root: HTMLElement;
  let refreshes: number;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render(items: CustomerEvidence[] = [], closed = false) {
    fixture = TestBed.createComponent(DisputeEvidencePanel);
    fixture.componentRef.setInput('disputeId', disputeId);
    fixture.componentRef.setInput('closed', closed);
    refreshes = 0;
    fixture.componentInstance.refreshRequested.subscribe(() => refreshes++);
    fixture.detectChanges();
    http.expectOne(listUrl).flush({ items });
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  function choose(file: File, note = '') {
    const input = root.querySelector<HTMLInputElement>('#evidence-file')!;
    Object.defineProperty(input, 'files', { configurable: true, value: { length: 1, item: () => file } });
    input.dispatchEvent(new Event('change'));
    const noteInput = root.querySelector<HTMLInputElement>('#evidence-note')!;
    noteInput.value = note;
    noteInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit() {
    root.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }

  const pdf = () => new File([new Uint8Array(100)], 'proof.pdf', { type: 'application/pdf' });

  it('lists documents with same-origin download links', () => {
    render([stored]);
    const link = root.querySelector<HTMLAnchorElement>('[data-testid="evidence-download"]')!;
    expect(link.getAttribute('href')).toBe(`/api/v1/disputes/${disputeId}/evidence/ev-1/content`);
    expect(link.getAttribute('download')).toBe('receipt.pdf');
    expect(root.textContent).toContain('2.0 KB');
    expect(root.textContent).toContain('Note: Shop receipt');
    expect(root.querySelector('[data-testid="evidence-empty"]')).toBeNull();
  });

  it('shows an empty state and retries a failed list', () => {
    fixture = TestBed.createComponent(DisputeEvidencePanel);
    fixture.componentRef.setInput('disputeId', disputeId);
    fixture.detectChanges();
    http.expectOne(listUrl).flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Unable to load your documents');
    root.querySelector<HTMLButtonElement>('[data-testid="evidence-retry"]')!.click();
    http.expectOne(listUrl).flush({ items: [] });
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="evidence-empty"]')).not.toBeNull();
  });

  it('hides the upload form for a closed dispute but keeps downloads', () => {
    render([stored], true);
    expect(root.querySelector('form')).toBeNull();
    expect(root.querySelector('[data-testid="evidence-closed"]')?.textContent).toContain('closed');
    expect(root.querySelector('[data-testid="evidence-download"]')).not.toBeNull();
  });

  it('stops offering uploads once the five-file limit is reached', () => {
    render(Array.from({ length: 5 }, (_, index) => ({ ...stored, id: `ev-${index}` })));
    expect(root.querySelector('form')).toBeNull();
    expect(root.querySelector('[data-testid="evidence-limit"]')?.textContent).toContain('limit of 5');
  });

  describe('client pre-checks (UX only; the server is authoritative)', () => {
    it('rejects empty, oversized and unsupported files without sending anything', () => {
      render();
      const huge = new File([new Uint8Array(1)], 'big.pdf', { type: 'application/pdf' });
      Object.defineProperty(huge, 'size', { value: 5 * 1024 * 1024 + 1 });
      for (const [file, message] of [
        [new File([], 'empty.pdf', { type: 'application/pdf' }), 'empty'],
        [huge, 'larger than 5 MB'],
        [new File([new Uint8Array(5)], 'notes.txt', { type: 'text/plain' }), 'Only PDF, PNG or JPEG'],
      ] as const) {
        choose(file);
        expect(root.querySelector('#evidence-error [role="alert"]')?.textContent).toContain(message);
        expect(root.querySelector('#evidence-file')?.getAttribute('aria-invalid')).toBe('true');
        submit();
        http.expectNone(listUrl);
      }
    });

    it('accepts exactly 5 MiB and falls back to the extension when the browser gives no type', () => {
      const edge = new File([new Uint8Array(1)], 'edge.png', { type: 'image/png' });
      Object.defineProperty(edge, 'size', { value: 5 * 1024 * 1024 });
      expect(evidenceFileProblem(edge, 0)).toBeNull();
      expect(evidenceFileProblem(new File([new Uint8Array(3)], 'scan.JPG', { type: '' }), 0)).toBeNull();
      expect(evidenceFileProblem(new File([new Uint8Array(3)], 'scan.exe', { type: '' }), 0)).not.toBeNull();
      expect(evidenceFileProblem(pdf(), 5)).toContain('maximum of 5');
    });

    it('asks for a file before submitting', () => {
      render();
      submit();
      expect(root.querySelector('#evidence-error [role="alert"]')?.textContent).toContain('Choose a document');
    });
  });

  describe('uploading', () => {
    function startUpload(note = 'Front and back'): TestRequest {
      render([stored]);
      choose(pdf(), note);
      submit();
      return http.expectOne(request => request.method === 'POST' && request.url === listUrl);
    }

    it('posts multipart form data without a manual Content-Type, shows progress and appends the result', () => {
      const request = startUpload();
      const body = request.request.body as FormData;
      expect(body).toBeInstanceOf(FormData);
      expect((body.get('file') as File).name).toBe('proof.pdf');
      expect(body.get('note')).toBe('Front and back');
      expect(request.request.headers.has('Content-Type')).toBe(false);
      expect(root.querySelector('button[type="submit"]')?.hasAttribute('disabled')).toBe(true);
      expect(root.querySelector<HTMLInputElement>('#evidence-file')!.disabled).toBe(true);

      request.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
      fixture.detectChanges();
      expect(root.querySelector<HTMLProgressElement>('[data-testid="evidence-progress"] progress')!.value).toBe(40);
      expect(root.querySelector('[data-testid="evidence-progress"]')?.textContent).toContain('40%');

      request.flush({ id: 'ev-2', fileName: 'proof.pdf', contentType: 'application/pdf', sizeBytes: 100, uploadedAt: '2026-11-13T10:00:00Z' });
      fixture.detectChanges();
      expect(root.querySelectorAll('.evidence-list li')).toHaveLength(2);
      expect(root.querySelector('[data-testid="evidence-status"]')?.textContent).toContain('proof.pdf was uploaded');
      expect(root.querySelector('[data-testid="evidence-progress"]')).toBeNull();
      expect(root.querySelector<HTMLInputElement>('#evidence-note')!.value).toBe('');
      expect(root.querySelector('button[type="submit"]')?.hasAttribute('disabled')).toBe(false);
    });

    it('omits an empty note and ignores a second submit while uploading', () => {
      render();
      choose(pdf(), '  ');
      submit();
      submit();
      const request = http.expectOne(candidate => candidate.method === 'POST');
      expect((request.request.body as FormData).has('note')).toBe(false);
      request.flush({ id: 'ev-3', fileName: 'proof.pdf', contentType: 'application/pdf', sizeBytes: 100, uploadedAt: '2026-11-13T10:00:00Z' });
    });

    for (const [status, code, message] of [
      [400, 'evidence.invalidFile', 'could not be accepted'],
      [409, 'evidence.limitReached', 'maximum of 5 files'],
      [422, 'evidence.rejectedByScan', 'rejected by our security scan'],
      [503, 'evidence.scanUnavailable', 'scanning is unavailable'],
      [409, 'dispute.closed', 'dispute is closed'],
      [500, 'unexpected', 'upload failed'],
    ] as const) {
      it(`explains ${code} and lets the customer try again`, () => {
        const request = startUpload();
        request.flush({ code }, { status, statusText: 'Error' });
        // A refused limit means the list was stale, so it is reloaded.
        if (code === 'evidence.limitReached') http.expectOne(listUrl).flush({ items: [stored] });
        fixture.detectChanges();
        expect(root.querySelector('#evidence-error [role="alert"]')?.textContent).toContain(message);
        expect(root.querySelector('button[type="submit"]')?.hasAttribute('disabled')).toBe(false);
        expect(root.querySelectorAll('.evidence-list li')).toHaveLength(1);
        expect(refreshes).toBe(code === 'dispute.closed' ? 1 : 0);
      });
    }
  });

  it('formats file sizes', () => {
    expect([formatFileSize(10), formatFileSize(1536), formatFileSize(5 * 1024 * 1024)]).toEqual(['10 B', '1.5 KB', '5.0 MB']);
  });
});
