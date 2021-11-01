
; include the file below to set if we are building the C version of the demo
; the C version of the demo simply replaces small portions of the code with a c version

.include "demo_ca65.inc"

.ifndef FAMISTUDIO_DEMO_USE_C
FAMISTUDIO_DEMO_USE_C = 0
.endif
.if FAMISTUDIO_DEMO_USE_C
; functions that the C library expects 
.export __STARTUP__:absolute=1
; this is for the C stack and are set in the mapper file
.import __STACK_START__, __STACKSIZE__
.include "zeropage.inc"

; Functions defined in C (using C decl instead of fastcall)
.import _play_song, _update, _init

; Variables and functions used in the C code must be prefixed with underscore
; so re-export the necessary ones here
.exportzp _gamepad_pressed=gamepad_pressed, _p0=p0
.exportzp sp
.export _song_title_silver_surfer=song_title_silver_surfer
.export _song_title_jts=song_title_jts
.export _song_title_shatterhand=song_title_shatterhand
.export _update_title=update_title
.endif

.segment "HEADER"
INES_MAPPER = 0 ; 0 = NROM
INES_MIRROR = 1 ; 0 = horizontal mirroring, 1 = vertical mirroring
INES_SRAM   = 0 ; 1 = battery backed SRAM at $6000-7FFF

.byte 'N', 'E', 'S', $1A ; ID 
.byte $02 ; 16k PRG bank count
.byte $01 ; 8k CHR bank count
.byte INES_MIRROR | (INES_SRAM << 1) | ((INES_MAPPER & $f) << 4)
.byte (INES_MAPPER & %11110000)
.byte $0, $0, $0, $0, $0, $0, $0, $0 ; padding

.segment "ZEROPAGE"
nmi_lock:           .res 1 ; prevents NMI re-entry
nmi_count:          .res 1 ; is incremented every NMI
nmi_ready:          .res 1 ; set to 1 to push a PPU frame update, 2 to turn rendering off next NMI
nmt_row_update_len: .res 1 ; number of bytes in nmt_row_update buffer
nmt_col_update_len: .res 1 ; number of bytes in nmt_col_update buffer
scroll_x:           .res 1 ; x scroll position
scroll_y:           .res 1 ; y scroll position
scroll_nmt:         .res 1 ; nametable select (0-3 = $2000,$2400,$2800,$2C00)
gamepad:            .res 1
gamepad_previous:   .res 1
gamepad_pressed:    .res 1
song_index:         .res 1
pause_flag:         .res 1

; General purpose temporary vars.
r0: .res 1
r1: .res 1
r2: .res 1
r3: .res 1
r4: .res 1

; General purpose pointers.
p0: .res 2
.if FAMISTUDIO_DEMO_USE_C
; Pointer to the C stack head
sp = $80
.endif

.segment "RAM"
; TODO: These 2 arent actually used at the same time... unify.
nmt_col_update: .res 128 ; nametable update entry buffer for PPU update (column mode)
nmt_row_update: .res 128 ; nametable update entry buffer for PPU update (column mode)
palette:        .res 32  ; palette buffer for PPU update

.segment "OAM"
oam: .res 256        ; sprite OAM data to be uploaded by DMA

.segment "CODE"

; FamiStudio config.
FAMISTUDIO_CFG_EXTERNAL       = 1
FAMISTUDIO_CFG_DPCM_SUPPORT   = 1
FAMISTUDIO_CFG_SFX_SUPPORT    = 1 
FAMISTUDIO_CFG_SFX_STREAMS    = 2
FAMISTUDIO_CFG_EQUALIZER      = 1
FAMISTUDIO_USE_VOLUME_TRACK   = 1
FAMISTUDIO_USE_PITCH_TRACK    = 1
FAMISTUDIO_USE_SLIDE_NOTES    = 1
FAMISTUDIO_USE_VIBRATO        = 1
FAMISTUDIO_USE_ARPEGGIO       = 1
FAMISTUDIO_CFG_SMOOTH_VIBRATO = 1
FAMISTUDIO_DPCM_OFF           = $e000

.if FAMISTUDIO_DEMO_USE_C
FAMISTUDIO_CFG_C_BINDINGS = 1
.endif

; CA65-specifc config.
.define FAMISTUDIO_CA65_ZP_SEGMENT   ZEROPAGE
.define FAMISTUDIO_CA65_RAM_SEGMENT  RAM
.define FAMISTUDIO_CA65_CODE_SEGMENT CODE

.include "..\famistudio_ca65.s"

; Our single screen.
screen_data_rle:
.incbin "demo.rle"

default_palette:
.incbin "demo.pal"
.incbin "demo.pal"

; Silver Surfer - BGM 2
song_title_silver_surfer:
    .byte $ff, $ff, $ff, $12, $22, $25, $2f, $1e, $2b, $ff, $12, $2e, $2b, $1f, $1e, $2b, $ff, $4c, $ff, $01, $06, $0c, $ff, $36, $ff, $ff, $ff, $ff

; Journey To Silius - Menu
song_title_jts:
    .byte $ff, $ff, $09, $28, $2e, $2b, $27, $1e, $32, $ff, $13, $28, $ff, $12, $22, $25, $22, $2e, $2c, $ff, $4c, $ff, $0c, $1e, $27, $2e, $ff, $ff

; Shatterhand - Final Area
song_title_shatterhand:
    .byte $ff, $ff, $12, $21, $1a, $2d, $2d, $1e, $2b, $21, $1a, $27, $1d, $ff, $4c, $ff, $05, $22, $27, $1a, $25, $ff, $00, $2b, $1e, $1a, $ff, $ff

NUM_SONGS = 3

_exit:
reset:

    sei       ; mask interrupts
    lda #0
    sta $2000 ; disable NMI
    sta $2001 ; disable rendering
    sta $4015 ; disable APU sound
    sta $4010 ; disable DMC IRQ
    lda #$40
    sta $4017 ; disable APU IRQ
    cld       ; disable decimal mode
    ldx #$FF
    txs       ; initialize stack
    ; wait for first vblank
    bit $2002
    @wait_vblank_loop:
        bit $2002
        bpl @wait_vblank_loop
    ; clear all RAM to 0
    lda #0
    ldx #0
    @clear_ram_loop:
        sta $0000, X
        sta $0100, X
        sta $0200, X
        sta $0300, X
        sta $0400, X
        sta $0500, X
        sta $0600, X
        sta $0700, X
        inx
        bne @clear_ram_loop
    ; place all sprites offscreen at Y=255
    lda #255
    ldx #0
    @clear_oam_loop:
        sta oam, X
        inx
        inx
        inx
        inx
        bne @clear_oam_loop
    .if FAMISTUDIO_DEMO_USE_C
        ; Initialize the C stack
        lda #<(__STACK_START__+__STACKSIZE__)
        sta	sp
        lda	#>(__STACK_START__+__STACKSIZE__)
        sta	sp + 1
        lda #$40
    .endif
    ; wait for second vblank
    @wait_vblank_loop2:
        bit $2002
        bpl @wait_vblank_loop2
    ; NES is initialized, ready to begin!
    ; enable the NMI for graphical updates, and jump to our main program
    lda #%10001000
    sta $2000
    jmp main

nmi:
    ; save registers
    pha
    txa
    pha
    tya
    pha
    ; prevent NMI re-entry
    lda nmi_lock
    beq @lock_nmi
    jmp @nmi_end
@lock_nmi:
    lda #1
    sta nmi_lock
    inc nmi_count
    lda nmi_ready
    bne @check_rendering_off ; nmi_ready == 0 not ready to update PPU
    jmp @ppu_update_end
@check_rendering_off:
    cmp #2 ; nmi_ready == 2 turns rendering off
    bne @oam_dma
        lda #%00000000
        sta $2001
        ldx #0
        stx nmi_ready
        jmp @ppu_update_end
@oam_dma:
    ; sprite OAM DMA
    ldx #0
    stx $2003
    lda #>oam
    sta $4014

    ; nametable update (column)
    @col_update:
        ldx #0
        cpx nmt_col_update_len
        beq @row_update
        lda #%10001100
        sta $2000 ; set vertical nametable increment
        @nmt_col_update_loop:
            lda nmt_col_update, x
            inx
            sta $2006
            lda nmt_col_update, x
            inx
            sta $2006
            ldy nmt_col_update, x
            inx
            @col_loop:
                lda nmt_col_update, x
                inx
                sta $2007
                dey
                bne @col_loop
            cpx nmt_col_update_len
            bcc @nmt_col_update_loop
        lda #0
        sta nmt_col_update_len

    ; nametable update (row)
    @row_update:
        lda #%10001000
        sta $2000 ; set horizontal nametable increment
        ldx #0
        cpx nmt_row_update_len
        bcs @palettes
        @nmt_row_update_loop:
            lda nmt_row_update, x
            inx
            sta $2006
            lda nmt_row_update, x
            inx
            sta $2006
            ldy nmt_row_update, x
            inx
            @row_loop:
                lda nmt_row_update, x
                inx
                sta $2007
                dey
                bne @row_loop
            cpx nmt_row_update_len
            bcc @nmt_row_update_loop
        lda #0
        sta nmt_row_update_len

    ; palettes
    @palettes:
        lda #%10001000
        sta $2000 ; set horizontal nametable increment  
        lda $2002
        lda #$3F
        sta $2006
        ldx #0
        stx $2006 ; set 0PPU address to $3F00
        @pal_loop:
            lda palette, X
            sta $2007
            inx
            cpx #32
            bne @pal_loop

@scroll:
    lda scroll_nmt
    and #%00000011 ; keep only lowest 2 bits to prevent error
    ora #%10001000
    sta $2000
    lda scroll_x
    sta $2005
    lda scroll_y
    sta $2005
    ; enable rendering
    lda #%00011110
    sta $2001
    ; flag PPU update complete
    ldx #0
    stx nmi_ready
@ppu_update_end:
    ; if this engine had music/sound, this would be a good place to play it
    ; unlock re-entry flag
    lda #0
    sta nmi_lock
@nmi_end:
    ; restore registers and return
    pla
    tay
    pla
    tax
    pla
    rti

irq:
    rti

; ppu_update: waits until next NMI, turns rendering on (if not already), uploads OAM, palette, and nametable update to PPU
ppu_update:
    lda #1
    sta nmi_ready
    @wait:
        lda nmi_ready
        bne @wait
    rts

; ppu_skip: waits until next NMI, does not update PPU
ppu_skip:
    lda nmi_count
    @wait:
        cmp nmi_count
        beq @wait
    rts

; ppu_off: waits until next NMI, turns rendering off (now safe to write PPU directly via $2007)
ppu_off:
    lda #2
    sta nmi_ready
    @wait:
        lda nmi_ready
        bne @wait
    rts

PAD_A      = $01
PAD_B      = $02
PAD_SELECT = $04
PAD_START  = $08
PAD_U      = $10
PAD_D      = $20
PAD_L      = $40
PAD_R      = $80

gamepad_poll:
    ; strobe the gamepad to latch current button state
    lda #1
    sta $4016
    lda #0
    sta $4016
    ; read 8 bytes from the interface at $4016
    ldx #8
    @gamepad_loop:
        pha
        lda $4016
        ; combine low two bits and store in carry bit
        and #%00000011
        cmp #%00000001
        pla
        ; rotate carry into gamepad variable
        ror a
        dex
        bne @gamepad_loop
    sta gamepad
    rts

gamepad_poll_dpcm_safe:
    
    lda gamepad
    sta gamepad_previous
    jsr gamepad_poll
    @reread:
        lda gamepad
        pha
        jsr gamepad_poll
        pla
        cmp gamepad
        bne @reread

    @toggle:
    eor gamepad_previous
    and gamepad
    sta gamepad_pressed

    rts

play_song:
.if FAMISTUDIO_DEMO_USE_C
    jsr _play_song
    rts
update_title:
    ldx #2
    ldy #15
    jsr draw_text
    jsr ppu_update
    rts
.else
    @text_ptr = p0

    lda song_index
    cmp #1
    beq @journey_to_silius
    cmp #2
    beq @shatterhand

    ; Here since both of our songs came from different FamiStudio projects, 
    ; they are actually 3 different song data, with a single song in each.
    ; For a real game, if would be preferable to export all songs together
    ; so that instruments shared across multiple songs are only exported once.
    @silver_surfer:
        lda #<song_title_silver_surfer
        sta @text_ptr+0
        lda #>song_title_silver_surfer
        sta @text_ptr+1
        ldx #.lobyte(music_data_silver_surfer_c_stephen_ruddy)
        ldy #.hibyte(music_data_silver_surfer_c_stephen_ruddy)
        jmp @play_song

    @journey_to_silius:
        lda #<song_title_jts
        sta @text_ptr+0
        lda #>song_title_jts
        sta @text_ptr+1
        ldx #.lobyte(music_data_journey_to_silius)
        ldy #.hibyte(music_data_journey_to_silius)
        jmp @play_song

    @shatterhand:
        lda #<song_title_shatterhand
        sta @text_ptr+0
        lda #>song_title_shatterhand
        sta @text_ptr+1
        ldx #.lobyte(music_data_shatterhand)
        ldy #.hibyte(music_data_shatterhand)
        jmp @play_song
    
    @play_song:
    lda #1 ; NTSC
    jsr famistudio_init
    lda #0
    jsr famistudio_music_play

    ;update title.
    ldx #2
    ldy #15
    jsr draw_text
    jsr ppu_update
    rts
.endif

equalizer_lookup:
    .byte $f0, $f0, $f0, $f0 ; 0
    .byte $f0, $f0, $f0, $b8 ; 1
    .byte $f0, $f0, $f0, $c8 ; 2
    .byte $f0, $f0, $b8, $c8 ; 3
    .byte $f0, $f0, $c8, $c8 ; 4
    .byte $f0, $b8, $c8, $c8 ; 5
    .byte $f0, $c8, $c8, $c8 ; 6
    .byte $b8, $c8, $c8, $c8 ; 7
    .byte $c8, $c8, $c8, $c8 ; 8
equalizer_color_lookup:
    .byte $01, $02, $00, $02, $01

; a = channel to update
update_equalizer:
    
    @pos_x = r0
    @color_offset = r1

    tay
    lda equalizer_color_lookup, y
    sta @color_offset
    tya

    ; compute x position.
    asl a
    asl a
    sta @pos_x

    ; compute lookup index.
    lda famistudio_chn_note_counter, y
    asl a
    asl a
    tay

    ; compute 2 addresses
    ldx nmt_col_update_len
    lda #$22
    sta nmt_col_update,x
    sta nmt_col_update+7,x
    lda #$47
    clc
    adc @pos_x
    sta nmt_col_update+1,x
    adc #1
    sta nmt_col_update+8,x
    lda #4
    sta nmt_col_update+2,x
    sta nmt_col_update+9,x

    lda equalizer_lookup, y
    adc @color_offset
    sta nmt_col_update+3,x
    sta nmt_col_update+10,x
    lda equalizer_lookup+1, y
    adc @color_offset
    sta nmt_col_update+4,x
    sta nmt_col_update+11,x
    lda equalizer_lookup+2, y
    adc @color_offset
    sta nmt_col_update+5,x
    sta nmt_col_update+12,x
    lda equalizer_lookup+3, y
    adc @color_offset
    sta nmt_col_update+6,x
    sta nmt_col_update+13,x
    
    lda #14
    adc nmt_col_update_len
    sta nmt_col_update_len 

    rts

main:

    ldx #0
    @palette_loop:
        lda default_palette, X
        sta palette, X
        inx
        cpx #32
        bcc @palette_loop
    
    jsr setup_background

    lda #0 ; song zero.
    sta song_index
    jsr play_song

    jsr ppu_update

.if FAMISTUDIO_DEMO_USE_C
    jsr _init
.else
    ; Load SFX
    ldx #<sounds
    ldy #>sounds
    jsr famistudio_sfx_init
.endif

@loop:
    jsr gamepad_poll_dpcm_safe

.if FAMISTUDIO_DEMO_USE_C
    jsr _update
.else
    @check_right:
        lda gamepad_pressed
        and #PAD_R
        beq @check_left

        ; dont go beyond last song.
        lda song_index
        cmp #(NUM_SONGS - 1)
        beq @draw

        ; next song.
        clc
        adc #1
        sta song_index
        jsr play_song
        jmp @draw_done 

    @check_left:
        lda gamepad_pressed
        and #PAD_L
        beq @check_select

        ; dont go below zero
        lda song_index
        beq @draw

        sec
        sbc #1
        sta song_index
        jsr play_song
        jmp @draw_done 

    @check_select:
        lda gamepad_pressed
        and #PAD_SELECT
        beq @check_start

        ; Undocumented: selects plays a SFX sample when journey to silius is loaded.
        lda song_index
        cmp #1
        bne @draw

        lda #21
        jsr famistudio_sfx_sample_play
        jmp @draw

    @check_start:
        lda gamepad_pressed
        and #PAD_START
        beq @check_a

        lda #1
        eor pause_flag
        sta pause_flag

        jsr famistudio_music_pause
        jmp @draw

    @check_a:
        lda gamepad_pressed
        and #PAD_A
        beq @check_b

        lda #0
        ldx #FAMISTUDIO_SFX_CH0
        jsr famistudio_sfx_play
        beq @draw

    @check_b:
        lda gamepad_pressed
        and #PAD_B
        beq @draw

        lda #1
        ldx #FAMISTUDIO_SFX_CH1
        jsr famistudio_sfx_play
        beq @draw
@draw:
    jsr famistudio_update ; TODO: Call in NMI.
.endif    
    lda #0
    jsr update_equalizer
    lda #1
    jsr update_equalizer
    lda #2
    jsr update_equalizer
    lda #3
    jsr update_equalizer
    lda #4
    jsr update_equalizer

@draw_done:

    jsr ppu_update
    jmp @loop

; Shiru's code.
; x = lo byte of RLE data addr
; y = hi byte of RLE data addr
rle_decompress:

    @rle_lo   = r0
    @rle_high = r1
    @rle_tag  = r2
    @rle_byte = r3

    stx @rle_lo
    sty @rle_high
    ldy #0
    jsr rle_read_byte
    sta @rle_tag
@loop:
    jsr rle_read_byte
    cmp @rle_tag
    beq @is_rle
    sta $2007
    sta @rle_byte
    bne @loop
@is_rle:
    jsr rle_read_byte
    cmp #0
    beq @done
    tax
    lda @rle_byte
@rle_loop:
    sta $2007
    dex
    bne @rle_loop
    beq @loop
@done: ;.4
    rts

rle_read_byte:

    @rle_lo   = r0
    @rle_high = r1

    lda (@rle_lo),y
    inc @rle_lo
    bne @done
    inc @rle_high
@done:
    rts

; Draws text with rendering on.
; x/y = tile position
; p0  = pointer to text data.
draw_text:

    @temp_x   = r2
    @temp     = r3
    @text_ptr = p0
    
    stx @temp_x
    ldx nmt_row_update_len
    tya
    lsr
    lsr
    lsr
    ora #$20 ; high bits of Y + $20
    sta nmt_row_update,x
    inx
    tya
    asl
    asl
    asl
    asl
    asl
    sta @temp
    lda @temp_x
    ora @temp
    sta nmt_row_update,x
    inx
    lda #28 ; all our strings have 28 characters.
    sta nmt_row_update,x
    inx

    ldy #0
    @text_loop:
        lda (@text_ptr),y
        sta nmt_row_update,x
        inx
        iny
        cpy #28
        bne @text_loop

    stx nmt_row_update_len
    rts

setup_background:

    ; first nametable, start by clearing to empty
    lda $2002 ; reset latch
    lda #$20
    sta $2006
    lda #$00
    sta $2006

    ; BG image.
    ldx #<screen_data_rle
    ldy #>screen_data_rle
    jsr rle_decompress

    ; Add a few sprites to the FamiStudio logo.
    lda #80
    sta oam+3
    lda #15
    sta oam+0
    lda #$81
    sta oam+1
    lda #1
    sta oam+2

    lda #72
    sta oam+7
    lda #23
    sta oam+4
    lda #$90
    sta oam+5
    lda #1
    sta oam+6

    lda #88
    sta oam+11
    lda #23
    sta oam+8
    lda #$92
    sta oam+9
    lda #1
    sta oam+10

    rts

.segment "SONG1"
song_silver_surfer:
.include "song_silver_surfer_ca65.s"

sfx_data:
.include "sfx_ca65.s"

.segment "SONG2"
song_journey_to_silius:
.include "song_journey_to_silius_ca65.s"

.segment "SONG3"
song_shatterhand:
.include "song_shatterhand_ca65.s"

.segment "DPCM"
.incbin "song_journey_to_silius_ca65.dmc"

.segment "VECTORS"
.word nmi
.word reset
.word irq

.segment "CHARS"
.incbin "demo.chr"
.incbin "demo.chr"
