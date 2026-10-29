"""Builds docs/Talkie-Manual.pdf from the screenshots in docs/screenshots.

Usage: python3 docs/tools/build_manual.py
Requires: reportlab, Pillow
"""
import os
import tempfile
from datetime import date

from PIL import Image as PILImage
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, Frame, Image, KeepTogether, NextPageTemplate, PageBreak,
                                PageTemplate, Paragraph, Spacer, Table, TableStyle)
from reportlab.platypus.tableofcontents import TableOfContents

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SHOTS = os.path.join(ROOT, 'docs', 'screenshots')
OUT = os.path.join(ROOT, 'docs', 'Talkie-Manual.pdf')

# ---------- Fonts & colors ----------
FONT, BOLD = 'Helvetica', 'Helvetica-Bold'
for regular, bold in [('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf')]:
    if os.path.exists(regular) and os.path.exists(bold):
        pdfmetrics.registerFont(TTFont('Body', regular))
        pdfmetrics.registerFont(TTFont('Body-Bold', bold))
        pdfmetrics.registerFontFamily('Body', normal='Body', bold='Body-Bold', italic='Body', boldItalic='Body-Bold')
        FONT, BOLD = 'Body', 'Body-Bold'

BLUE = colors.HexColor('#5B9BEA')
DARK_BLUE = colors.HexColor('#3F7FD1')
INK = colors.HexColor('#2C3E5A')
MUTED = colors.HexColor('#6B7D99')
PALE = colors.HexColor('#EAF2FD')
LINE = colors.HexColor('#DCE7F7')

styles = {
    'h1': ParagraphStyle('h1', fontName=BOLD, fontSize=22, leading=28, textColor=DARK_BLUE, spaceBefore=4, spaceAfter=10),
    'title': ParagraphStyle('title', fontName=BOLD, fontSize=22, leading=28, textColor=DARK_BLUE, spaceAfter=10),
    'h2': ParagraphStyle('h2', fontName=BOLD, fontSize=14, leading=19, textColor=INK, spaceBefore=12, spaceAfter=6),
    'body': ParagraphStyle('body', fontName=FONT, fontSize=10, leading=15, textColor=INK, spaceAfter=6),
    'bullet': ParagraphStyle('bullet', fontName=FONT, fontSize=10, leading=15, textColor=INK, leftIndent=14, bulletIndent=4, spaceAfter=2),
    'caption': ParagraphStyle('caption', fontName=FONT, fontSize=8.5, leading=12, textColor=MUTED, alignment=TA_CENTER, spaceBefore=3, spaceAfter=12),
    'note': ParagraphStyle('note', fontName=FONT, fontSize=9.5, leading=14, textColor=INK),
    'code': ParagraphStyle('code', fontName='Courier', fontSize=9, leading=12.5, textColor=INK),
    'toc1': ParagraphStyle('toc1', fontName=FONT, fontSize=11, leading=20, textColor=INK, leftIndent=6),
    'toc2': ParagraphStyle('toc2', fontName=FONT, fontSize=9.5, leading=15, textColor=MUTED, leftIndent=22),
}

PAGE_W, PAGE_H = A4
MARGIN = 18 * mm
CONTENT_W = PAGE_W - 2 * MARGIN


# ---------- Document with automatic table of contents ----------
class ManualDoc(BaseDocTemplate):
    def __init__(self, path):
        super().__init__(path, pagesize=A4, leftMargin=MARGIN, rightMargin=MARGIN, topMargin=20 * mm, bottomMargin=18 * mm,
                         title='Talkie - User & Administrator Manual', author='Talkie', subject='Talkie manual')
        frame = Frame(MARGIN, 18 * mm, CONTENT_W, PAGE_H - 38 * mm, id='main')
        self.addPageTemplates([
            PageTemplate(id='cover', frames=[frame], onPage=draw_cover),
            PageTemplate(id='normal', frames=[frame], onPage=draw_page),
        ])

    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and flowable.style.name in ('h1', 'h2'):
            level = 0 if flowable.style.name == 'h1' else 1
            text = flowable.getPlainText()
            key = f'h{self.seq.nextf("heading")}'
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text, key, level=level, closed=level > 0)
            self.notify('TOCEntry', (level, text, self.page, key))


def draw_cover(canvas, doc):
    canvas.saveState()
    canvas.setFillColor(PALE)
    canvas.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    canvas.setFillColor(BLUE)
    canvas.circle(PAGE_W - 30 * mm, PAGE_H - 20 * mm, 70 * mm, fill=1, stroke=0)
    canvas.setFillColor(colors.HexColor('#9CC9F5'))
    canvas.circle(25 * mm, 30 * mm, 45 * mm, fill=1, stroke=0)
    # Logo: a speech bubble with three dots.
    x, y = MARGIN, PAGE_H - 95 * mm
    canvas.setFillColor(DARK_BLUE)
    canvas.roundRect(x, y, 30 * mm, 22 * mm, 5 * mm, fill=1, stroke=0)
    p = canvas.beginPath()
    p.moveTo(x + 8 * mm, y + 1 * mm); p.lineTo(x + 6 * mm, y - 7 * mm); p.lineTo(x + 15 * mm, y + 1 * mm); p.close()
    canvas.drawPath(p, fill=1, stroke=0)
    canvas.setFillColor(colors.white)
    for i in range(3):
        canvas.circle(x + (8 + i * 7) * mm, y + 11 * mm, 2.2 * mm, fill=1, stroke=0)
    canvas.setFillColor(DARK_BLUE)
    canvas.setFont(BOLD, 44)
    canvas.drawString(MARGIN, PAGE_H - 125 * mm, 'Talkie')
    canvas.setFillColor(INK)
    canvas.setFont(FONT, 18)
    canvas.drawString(MARGIN, PAGE_H - 137 * mm, 'User & Administrator Manual')
    canvas.setFillColor(MUTED)
    canvas.setFont(FONT, 11)
    canvas.drawString(MARGIN, PAGE_H - 147 * mm, 'Chat, play and work together.')
    canvas.drawString(MARGIN, 40 * mm, f'Version 1.0  ·  {date.today():%B %Y}')
    canvas.drawString(MARGIN, 34 * mm, 'Blazor WebAssembly · ASP.NET Core · EF Core 10 · SQL Server 2025 · .NET Aspire 13')
    canvas.restoreState()


def draw_page(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(LINE)
    canvas.setLineWidth(0.6)
    canvas.line(MARGIN, PAGE_H - 14 * mm, PAGE_W - MARGIN, PAGE_H - 14 * mm)
    canvas.setFont(BOLD, 9)
    canvas.setFillColor(DARK_BLUE)
    canvas.drawString(MARGIN, PAGE_H - 11.5 * mm, 'Talkie')
    canvas.setFont(FONT, 9)
    canvas.setFillColor(MUTED)
    canvas.drawRightString(PAGE_W - MARGIN, PAGE_H - 11.5 * mm, 'User & Administrator Manual')
    canvas.line(MARGIN, 12 * mm, PAGE_W - MARGIN, 12 * mm)
    canvas.drawCentredString(PAGE_W / 2, 7.5 * mm, str(doc.page))
    canvas.restoreState()


# ---------- Content helpers ----------
story = []
figure_no = [0]


def h1(text):
    story.append(PageBreak())
    story.append(Paragraph(text, styles['h1']))


def h2(text):
    story.append(Paragraph(text, styles['h2']))


def p(text):
    story.append(Paragraph(text, styles['body']))


def bullets(*items):
    for item in items:
        story.append(Paragraph(item, styles['bullet'], bulletText='•'))
    story.append(Spacer(1, 4))


CROPS = tempfile.mkdtemp(prefix='talkie-manual-')


def shot(name, caption, width=1.0, crop=None):
    path = os.path.join(SHOTS, name + '.png')
    if not os.path.exists(path):
        p(f'<i>[missing screenshot {name}]</i>')
        return
    if crop:
        cropped = os.path.join(CROPS, name + '.png')
        with PILImage.open(path) as im:
            im.crop(crop).save(cropped)
        path = cropped
    with PILImage.open(path) as im:
        w, h = im.size
    max_w = CONTENT_W * width
    max_h = 120 * mm
    scale = min(max_w / w, max_h / h)
    figure_no[0] += 1
    img = Image(path, width=w * scale, height=h * scale)
    framed = Table([[img]], style=TableStyle([
        ('BOX', (0, 0), (-1, -1), 0.6, LINE),
        ('LEFTPADDING', (0, 0), (-1, -1), 0), ('RIGHTPADDING', (0, 0), (-1, -1), 0),
        ('TOPPADDING', (0, 0), (-1, -1), 0), ('BOTTOMPADDING', (0, 0), (-1, -1), 0),
    ]))
    story.append(KeepTogether([Spacer(1, 4), framed, Paragraph(f'Figure {figure_no[0]}. {caption}', styles['caption'])]))


def note(text, title='Tip'):
    t = Table([[Paragraph(f'<b>{title}</b>  {text}', styles['note'])]], colWidths=[CONTENT_W])
    t.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), PALE), ('LINEBEFORE', (0, 0), (0, -1), 3, BLUE),
        ('LEFTPADDING', (0, 0), (-1, -1), 10), ('RIGHTPADDING', (0, 0), (-1, -1), 10),
        ('TOPPADDING', (0, 0), (-1, -1), 7), ('BOTTOMPADDING', (0, 0), (-1, -1), 7),
    ]))
    story.append(Spacer(1, 2))
    story.append(t)
    story.append(Spacer(1, 8))


def table(rows, widths, header=True):
    data = [[Paragraph(str(c), styles['note']) for c in row] for row in rows]
    t = Table(data, colWidths=[w * CONTENT_W for w in widths], repeatRows=1 if header else 0)
    style = [
        ('GRID', (0, 0), (-1, -1), 0.5, LINE), ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('LEFTPADDING', (0, 0), (-1, -1), 6), ('RIGHTPADDING', (0, 0), (-1, -1), 6),
        ('TOPPADDING', (0, 0), (-1, -1), 4), ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
    ]
    if header:
        style.append(('BACKGROUND', (0, 0), (-1, 0), PALE))
    t.setStyle(TableStyle(style))
    story.append(t)
    story.append(Spacer(1, 8))


def code(*lines):
    t = Table([[Paragraph('<br/>'.join(l.replace('&', '&amp;').replace('<', '&lt;').replace(' ', '&nbsp;') for l in lines), styles['code'])]],
              colWidths=[CONTENT_W])
    t.setStyle(TableStyle([('BACKGROUND', (0, 0), (-1, -1), colors.HexColor('#F4F8FE')), ('BOX', (0, 0), (-1, -1), 0.5, LINE),
                           ('LEFTPADDING', (0, 0), (-1, -1), 8), ('TOPPADDING', (0, 0), (-1, -1), 6), ('BOTTOMPADDING', (0, 0), (-1, -1), 6)]))
    story.append(t)
    story.append(Spacer(1, 8))


# ---------- Cover & contents ----------
story.append(NextPageTemplate('normal'))
story.append(PageBreak())
story.append(Paragraph('Contents', styles['title']))
toc = TableOfContents()
toc.levelStyles = [styles['toc1'], styles['toc2']]
toc.dotsMinLevel = 0
story.append(toc)

# ---------- 1. Introduction ----------
h1('1. Welcome to Talkie')
p('Talkie is a chat platform for companies with a playful twist. Colleagues talk in direct messages, group chats and '
  'company channels, while a small game layer rewards them with <b>money</b> and <b>shards</b> that can be spent in the '
  'shop on boxes, avatar effects and shard boosters.')
p('This manual explains every screen of the application, for regular members, company owners and the app administrator.')
h2('Who is who')
table([
    ['Account', 'What it can do'],
    ['<b>Member</b>', 'Chat, use the AI assistant, view the company, report users, view stats, use the shop and go AFK.'],
    ['<b>Company owner</b>', 'Everything a member can, plus: add, edit and delete members, manage roles, and create, edit and delete channels on the company server.'],
    ['<b>admin@talkie.com</b>', 'The app administrator. Sees the special <b>pencil button</b> next to the avatar to configure the whole app, sees all reports and is the only one who can remove reports.'],
], [0.25, 0.75])
h2('Built-in accounts')
p('Talkie ships with one company, <b>Talkie</b>. No other company can use that name. Two accounts are created automatically:')
table([
    ['E-mail', 'Password', 'Role'],
    ['owner@talkie.com', 'fwkh837h@m1', 'Owner of the Talkie company'],
    ['admin@talkie.com', 'TalkieAdmin1! (configurable)', 'App administrator (pencil button)'],
], [0.33, 0.33, 0.34])
note('The admin password comes from the Aspire parameter <b>admin-password</b> and is only used when the account is first created. '
     'Change it after the first login via <b>Account &gt; Security</b>.', 'Security')
h2('Sidebar')
p('After logging in, the sidebar on the left gives access to the seven areas of Talkie:')
table([
    ['Area', 'Purpose'],
    ['Chat', 'Direct messages, group chats and your company server with its text channels.'],
    ['AI assistant', 'A simple assistant that knows Talkie, your stats and can search your chats.'],
    ['Company', 'Members and roles of your company.'],
    ['Reports', 'Report users who break the rules.'],
    ['Stats', 'Money, shards, playtime, chat messages sent and boxes opened.'],
    ['Shop', 'Exchange money and shards, buy boxes, avatar effects and shard boosters.'],
    ['AFK', 'Go away from keyboard and earn shards every minute.'],
], [0.22, 0.78])

# ---------- 2. Login ----------
h1('2. Logging in and creating a company')
h2('Login')
p('Open Talkie in your browser. The login screen has two buttons at the top: <b>Login</b> and <b>Create Company</b>. '
  'To log in, type your e-mail and password and press <b>Login</b> (or Enter). Click the eye icon to show the password.')
shot('01-login', 'The login screen.', 0.6, crop=(420, 190, 1020, 690))
h2('Create a company')
p('Choose <b>Create Company</b> to register a new company. You will become its owner and are logged in immediately. The form asks for:')
bullets('<b>Company name</b> - must be unique. <b>Talkie</b> is reserved and cannot be used (in any capitalisation).',
        '<b>Owner e-mail</b> - the e-mail address of the owner. It must not be in use yet.',
        '<b>Owner password</b> - at least 8 characters with a letter and a digit.',
        '<b>Username</b> and <b>Display name</b> of the owner.',
        '<b>Roles</b> (optional) - click the <b>+</b> button to create roles with a name and a color. Click a role chip to edit it, '
        'or its <b>x</b> to remove it. Roles can be changed later in the Company page.')
shot('02-create-company', 'Creating a company with three roles.', 0.6, crop=(420, 40, 1020, 840))
shot('03-role-dialog', 'The role dialog: pick a pastel preset or any custom color.', 0.6, crop=(480, 190, 960, 690))
shot('02b-reserved-name', 'The name Talkie is reserved.', 0.6)
p('When the company is created Talkie automatically sets up a <b>server</b> with the same name as the company, with the '
  'channels <b>#general</b> and <b>#random</b>, and sends a welcome e-mail to the owner.')

# ---------- 3. Layout ----------
h1('3. Finding your way around')
p('The top bar shows the Talkie logo, your <b>money</b> and <b>shards</b> (click them to jump to the shop or AFK) and your avatar. '
  'Click your avatar to open a small dropdown with your <b>name and e-mail</b>, a link to your <b>Account</b> and <b>Logout</b>.')
shot('12-user-menu', 'The avatar dropdown with name, e-mail, Account and Logout.')
p('When you are logged in as <b>admin@talkie.com</b> there is a special <b>pencil button</b> right next to the avatar. '
  'It opens the app configuration (see chapter 12).')
shot('38-admin-topbar', 'The pencil button is only visible for admin@talkie.com.', 0.6)
h2('Big numbers')
p('Everywhere in Talkie large numbers are shortened: 1,000 = <b>1K</b>, 1,000,000 = <b>1M</b>, then <b>1B</b> (billion), '
  '<b>1T</b> (trillion) and <b>1Q</b> (quadrillion). One decimal is shown when useful, e.g. 1,500 = 1.5K. Hover a number in '
  'Stats to see the exact value.')

# ---------- 4. Chat ----------
h1('4. Chat')
p('The chat page has two sections in its side panel: <b>Direct Messages</b> and <b>Servers</b>. The selected conversation or '
  'channel is shown on the right.')
shot('04-chat-channel', 'The chat page with the company server and the #general channel.')
h2('Direct messages')
p('Click the <b>+</b> next to <i>Direct Messages</i> and choose <b>New message</b>. Type the e-mail of the person you want '
  'to talk to (suggestions appear while typing) and press <b>Start chat</b>. You can message anyone on Talkie by e-mail, '
  'also people from other companies.')
shot('07-new-conversation-menu', 'The + menu: start a new message or a new group.', 0.6)
shot('08-new-direct-message', 'Starting a direct message by e-mail.')
h2('Groups')
p('Choose <b>New group</b> in the same menu. Give the group a name and add people by e-mail with the add button. '
  'You become the <b>group owner</b>: the owner can delete any message in the group (other members can only delete their own).')
shot('09-new-group', 'Creating a group with three people.')
shot('33-group-owner', 'As group owner, the delete action is available on every message in the group.')
h2('Writing messages')
bullets('Type in the box at the bottom and press <b>Enter</b> to send. <b>Shift+Enter</b> adds a new line.',
        '<b>Attach files</b> with the paperclip button (up to 10 MB per file).',
        '<b>Paste images</b> straight from the clipboard (Ctrl+V): they are uploaded and previewed before sending.',
        'Images are shown inline; click one to open it larger. Other files appear as a download card.')
shot('06-direct-message', 'A direct message with a reply, an image and a file attachment.')
h2('Replies, reactions and deleting')
p('Hover a message to show its actions: <b>Reply</b>, <b>React</b> and <b>Delete</b>. A reply shows a small preview of the '
  'original message above it. Reactions are shown as chips under the message; click a chip to add or remove your reaction.')
shot('05-message-actions', 'Hovering a message shows reply, react and delete.')
shot('05b-emoji-picker', 'Picking an emoji reaction.')
p('Deleted messages are replaced by "This message was deleted." for everyone. Messages appear for the other people in real '
  'time (no refresh needed).')
h2('Servers and text channels')
p('Every company has one server, named after the company. Company owners see a <b>+</b> next to <i>Text channels</i> to create '
  'a channel, and a gear icon on each channel to edit or delete it. A channel has:')
bullets('<b>Name</b> and <b>description</b>.',
        '<b>Roles</b> - the roles that may see the channel. Leave all roles unselected so everyone can see it. Restricted channels show a lock icon. '
        'You can create new roles right from the dialog with <b>New role</b>; they use the same role list as the company.',
        '<b>Permissions</b> - per role: <b>Send messages</b>, <b>Delete messages</b> and <b>React with emoji</b>. '
        'By default <b>@everyone</b> can send messages and react with emoji. A member gets a permission when @everyone or any of their roles has it. '
        'The company owner can always do everything.')
shot('10-create-channel', 'Creating a channel visible to two roles, with extra permissions for Member.')
shot('32-read-only-channel', 'A channel where your roles cannot send messages is read-only.')
h2('Searching messages')
p('Click the search icon in the chat header to search every message you can read. Search is <b>semantic</b>: messages are '
  'stored with an embedding in SQL Server 2025\'s native <b>vector</b> type and ranked by cosine similarity, so related words '
  'match too (e.g. "lunch plans" finds "Who is up for lunch?"). Click a result to jump to that conversation.')
shot('11-semantic-search', 'Semantic message search with similarity scores.')

# ---------- 5. AI ----------
h1('5. AI assistant')
p('The AI assistant is a simple helper. Ask it how a feature works, ask for <b>"my stats"</b>, or say <b>"search"</b> '
  'followed by a topic to find chat messages. The conversation is saved; use <b>Clear chat</b> to start over.')
shot('13-ai-assistant', 'The AI assistant answering questions, showing stats and searching chats.')
note('Out of the box Talkie uses a built-in offline assistant. An administrator can connect any OpenAI-compatible model '
     '(OpenAI, Azure OpenAI, Ollama, LM Studio, ...) by setting <b>AI:Endpoint</b>, <b>AI:Model</b> and <b>AI:ApiKey</b> on the API. '
     'Relevant chat messages found with vector search are then given to the model as context.', 'Administrators')

# ---------- 6. Company ----------
h1('6. Company')
p('The Company page lists all members of your company with their roles. Use the search box to filter by name, e-mail or role.')
shot('14-company-members', 'The members of the Talkie company.')
h2('Adding members (owner)')
p('Click <b>Add member</b> and enter the <b>name</b>, <b>surname</b> and <b>e-mail</b>, and optionally select roles. '
  'Talkie generates a temporary password, shows it once and e-mails it to the new member.')
shot('16-add-member', 'Adding a member.')
shot('17-member-added', 'The temporary password is shown once and e-mailed.')
h2('Editing and deleting members (owner)')
p('Use the pencil icon to change a member\'s name, display name, username and roles. Use the delete icon to remove a member. '
  '<b>Deleting a member also deletes their account and e-mail address</b>: they are logged out immediately, their messages '
  'are removed and the e-mail can be used again. The owner and admin@talkie.com cannot be deleted.')
shot('18-edit-member', 'Editing a member and their roles.')
shot('19-delete-member', 'Deleting a member asks for confirmation.')
h2('Roles')
p('The <b>Roles</b> tab lists all roles with their color and number of members. Owners can create, rename, recolor and delete roles.')
shot('15-company-roles', 'Managing roles.')

# ---------- 7. Reports ----------
h1('7. Reports')
p('Everyone can report a user: click <b>Report a user</b>, choose the user by e-mail and describe the reason. '
  'Members see the reports they sent.')
shot('31-report-dialog', 'Reporting a user.')
shot('30-reports-member', 'A member sees their own reports.')
p('<b>admin@talkie.com</b> sees all reports from every company and is the only one who can remove reports, '
  'with the delete icon at the end of each row.')
shot('34-reports-admin', 'The admin view: all reports, with remove buttons.')

# ---------- 8. Stats ----------
h1('8. Stats')
p('The Stats page shows your <b>money</b>, <b>shards</b>, <b>playtime</b>, <b>chat messages sent</b> and <b>boxes opened</b>, '
  'plus a leaderboard of your company. Playtime counts how long you have had Talkie open.')
shot('20-stats', 'Your stats and the company leaderboard.')

# ---------- 9. Shop ----------
h1('9. Shop')
p('The shop has four tabs. Your balance is shown at the top right.')
h2('Boxes')
table([
    ['Box', 'Price', 'Possible rewards'],
    ['Common Box', '$200', '$100, $200, $500 or $1K'],
    ['Rare Box', '$500', '$200, $500, $1K, $5K or $10K'],
    ['Shard Box', '$1K', '100, 200, 500, 1K or 5K shards'],
], [0.25, 0.15, 0.60])
p('Buy a box and a dialog opens: <b>tap the box as fast as you can!</b> After 10 taps it opens and shows your reward '
  '(and how fast you were). Bigger rewards are rarer. Bought boxes you have not opened yet are listed under '
  '<i>Your unopened boxes</i>.')
shot('21-shop-boxes', 'The three boxes.')
shot('22-box-tapping', 'Tap the box to open it.', 0.7)
shot('23-box-reward', 'The reward.', 0.7)
h2('Exchange')
p('Buy shards with money, or buy money with shards. The exchange rates are set by the administrator (default: 1 shard = $10).')
shot('24-shop-exchange', 'Exchanging money and shards.')
h2('Avatar effects')
p('Effects decorate your avatar everywhere in Talkie - in chat, the member list and the top bar. A bought effect is equipped '
  'immediately; you own it forever and can switch or remove it at any time.')
table([
    ['Effect', 'Price', 'Description'],
    ['Energy Lightning', '$2.5K', 'Small yellow energy bolts strike around your avatar.'],
    ['Sparkles', '$1.5K', 'Glittering stars twinkle around your avatar.'],
    ['Floating Hearts', '$2K', 'Little hearts float up from your avatar.'],
    ['Frost Aura', '$3.5K', 'A cool icy glow pulses around your avatar.'],
    ['Blazing Fire', '$5K', 'Your avatar is wrapped in a flickering flame ring.'],
    ['Rainbow Halo', '$7.5K', 'A rotating rainbow ring circles your avatar.'],
    ['Galaxy Orbit', '$15K', 'Planets orbit your avatar.'],
], [0.25, 0.12, 0.63])
shot('25-shop-effects', 'Avatar effects, previewed on your own avatar.')
h2('Shard boosters')
p('Shard boosters multiply the shards you earn while AFK: <b>x2</b> ($10K) and <b>x4</b> ($50K). Boosters are permanent and the best one you own counts.')
shot('26-shop-boosters', 'Shard boosters.')

# ---------- 10. AFK ----------
h1('10. AFK')
p('Click <b>Go AFK</b>. Talkie asks <i>"Are you sure you want to go AFK?"</i> - choose <b>Yes</b> or <b>No</b>. While AFK '
  'you earn <b>1 shard every minute</b> (times your booster). The page shows a timer, the shards earned in this session and '
  'a progress bar to the next shard. Click <b>Stop AFK</b> when you are back.')
shot('27-afk-confirm', 'Confirming AFK.')
shot('28-afk-active', 'AFK in progress.')
note('AFK earnings are calculated on the server from timestamps, so they keep counting even if you close the browser - up to the '
     'AFK limit (24 hours by default).')

# ---------- 11. Account ----------
h1('11. Your account')
p('Open <b>Account</b> from the avatar dropdown. Here you can upload a <b>profile picture</b> (PNG, JPG, GIF or WebP up to 2 MB), '
  'remove it, change your display name, username, name and surname, and change your password.')
shot('29-account', 'The account page with profile picture and the Energy Lightning effect.')

# ---------- 12. Admin ----------
h1('12. Configuring the app (admin@talkie.com)')
p('Log in as <b>admin@talkie.com</b> and click the <b>pencil button</b> next to your avatar. The configuration dialog has four tabs:')
bullets('<b>General</b> - the app name, an announcement banner shown to everyone (also on the login screen) and whether new companies may be created.',
        '<b>Economy</b> - starting money and shards for new accounts, AFK shards per minute, the AFK limit in hours, the shard exchange rates and the box prices.',
        '<b>Companies</b> - all companies with owner and member count. Companies (except Talkie) can be deleted with all their data.',
        '<b>Grant</b> - give (or take) money and shards to any user.')
shot('35-admin-general', 'General settings.')
shot('36-admin-economy', 'Economy settings.')
shot('37-admin-companies', 'All companies.')

# ---------- 13. Running ----------
h1('13. Installing and running Talkie')
h2('Requirements')
bullets('.NET 10 SDK', 'Docker (or Podman) for SQL Server 2025 and MailPit', 'A modern browser')
h2('Start everything with .NET Aspire')
p('The Aspire AppHost starts SQL Server 2025, MailPit, the migration service, the API and the web front end in the right order:')
code('git clone https://github.com/tomvanoost/talkie.git', 'cd talkie', 'dotnet run --project src/Talkie.AppHost')
p('Open the Aspire dashboard link printed in the console and click the URL of the <b>web</b> resource. '
  'The <b>migrations</b> resource applies the database schema and creates the built-in accounts, then shows as Finished.')
shot('40-aspire-dashboard', 'The Aspire dashboard with all Talkie resources.')
h2('E-mail')
p('All e-mails (welcome mails and temporary passwords) are caught by <b>MailPit</b>. Open its URL from the dashboard to read them.')
shot('39-mailpit', 'MailPit showing the e-mail with a temporary password.')
h2('Configuration')
table([
    ['Setting', 'Where', 'Meaning'],
    ['auth-signing-key', 'Aspire parameter (generated)', 'Key used to sign login tokens (Auth:SigningKey on the API).'],
    ['admin-password', 'Aspire parameter', 'Initial password of admin@talkie.com.'],
    ['Seed:OwnerPassword', 'Migration service', 'Initial password of owner@talkie.com (default fwkh837h@m1).'],
    ['AI:Endpoint / AI:Model / AI:ApiKey', 'API', 'Optional OpenAI-compatible model for the AI assistant.'],
    ['ConnectionStrings:talkiedb', 'API, migrations', 'Provided by Aspire; SQL Server 2025 (compatibility level 170).'],
    ['ConnectionStrings:mailpit', 'API', 'Provided by Aspire; SMTP server for e-mail.'],
], [0.3, 0.25, 0.45])

# ---------- 14. Architecture ----------
h1('14. Architecture')
table([
    ['Layer', 'Technology'],
    ['Frontend', 'Blazor WebAssembly (.NET 10) with MudBlazor, pastel blue theme, English UI. Live updates via SignalR.'],
    ['Web host', 'ASP.NET Core host serving the WebAssembly app and proxying /api and /hubs to the API (YARP + service discovery).'],
    ['Backend', 'ASP.NET Core minimal APIs as a modular monolith: Auth, Company, Chat, Servers, AI, Reports, Stats, Shop, AFK and Admin modules.'],
    ['Data', 'EF Core 10 on SQL Server 2025, including the native vector(256) column used for semantic message search with VECTOR_DISTANCE.'],
    ['Hosting', '.NET Aspire 13 orchestrates SQL Server, MailPit, the migration service, the API and the web front end.'],
], [0.2, 0.8])
p('Security notes: passwords are hashed with ASP.NET Core Identity\'s PasswordHasher; the API issues signed JWTs; every request '
  'checks that the account still exists (so deleted members are logged out immediately); balances are changed with single '
  'conditional UPDATE statements so they can never go negative; box rewards are rolled on the server.')

# ---------- 15. FAQ ----------
h1('15. Frequently asked questions')
faq = [
    ('I cannot create a company called Talkie.', 'Talkie is the built-in company; the name is reserved.'),
    ('I don\'t see the + next to Text channels.', 'Only the company owner (and admin@talkie.com in the Talkie company) can create channels.'),
    ('I cannot type in a channel.', 'None of your roles has the <i>Send messages</i> permission there. Ask the company owner.'),
    ('A member forgot their password.', 'Delete and re-add the member to send a new temporary password, or have them use their last password.'),
    ('Do I keep earning shards when I close the browser while AFK?', 'Yes, up to the AFK limit set by the administrator (24 hours by default).'),
    ('Where is the pencil button?', 'It is only shown to admin@talkie.com, next to the avatar in the top bar.'),
]
for q, a in faq:
    story.append(KeepTogether([Paragraph(f'<b>{q}</b>', styles['body']), Paragraph(a, styles['body']), Spacer(1, 4)]))

doc = ManualDoc(OUT)
doc.multiBuild(story)
print('Wrote', OUT)
